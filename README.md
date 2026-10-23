# Prior authorization sample

A prior-authorization intake built with [Kaleido](https://github.com/no1ross/Kaleido): a caller identifies a member, says what imaging
service is needed, and the request moves from a generic **Intake** process to the **Radiology**
process that owns it, through the questions Radiology needs, to submission. The same steps serve a
web UI, an AI agent over MCP, and service-to-service calls.

This README is a short walkthrough. The conventions and details live in
[`AGENTS.md`](AGENTS.md) (auth model, history, audit fields, configuration, ports) and
[`docs/HANDOFF.md`](docs/HANDOFF.md) (the cross-processor handoff, backend and UI).

| Folder | Holds |
|---|---|
| `src/` | the services (`Services/*`), the router, the EventCollector, the seeder, and `Common` (the steps, handlers, query sources, data and dev auth they share) |
| `ui/` | the Angular app |
| `compose/` | the compose files for running everything in containers |
| `docs/` | design notes (the handoff) |
| `tests/` | tests, when there are any |
| `data/` | the SQLite databases, created at runtime (not committed) |

## What runs

```mermaid
flowchart LR
    ui["priorauth-ui<br/>(Angular)"]
    agent["AI agent<br/>(MCP client)"]
    router["Router :8080<br/>registry aggregate, /mcp, dev login"]
    intake["Intake :8085"]
    radiology["Radiology :8088"]
    history["History :8089"]
    member["Member :8084"]
    provider["Provider :8083"]
    others["CodeSet :8082<br/>Configuration :8087<br/>ReferenceData :8081"]
    events["EventCollector :8086"]

    ui --> router
    agent --> router
    router --> intake
    router --> radiology
    router --> history
    intake -- "handoff" --> radiology
    intake --> member
    radiology --> member
    radiology --> provider
    intake -- "records" --> history
    radiology -- "records" --> history
    intake --> others
    radiology --> others
    intake -. "events" .-> events
    radiology -. "events" .-> events
```

| Host | Role |
|---|---|
| Router | One entry point: the aggregated registry of every service, the MCP endpoint (`/mcp`), the dev login (`POST /auth/login`), and a reverse proxy to the services. The UI is a client in front of it, not served by it. |
| Intake | The front door process: start, find the member, capture the requested service, hand off to the product process. |
| Radiology | The product process: MRI/CT questions, providers, clinical questions, review, submit. |
| History | One record per request with its latest snapshot; the request list in the UI and the agent's first tool. |
| Member, Provider, CodeSet, Configuration, ReferenceData | Query sources (member search, provider search, procedure codes, questionnaires and routing rules, plans and states). |
| EventCollector | Receives the services' events (redacted PHI) so they can be inspected. |

Each service has its own SQLite database under `data/`; Intake and Radiology also keep Kaleido's
process state, outbox and idempotency records in a `*-process.db`.

## Run it

0. **Get the Kaleido packages.** The sample uses Kaleido's preview packages from its GitHub Packages
   feed (`nuget.config`). The feed needs a GitHub **classic** personal access token with
   `read:packages`, even though the packages are public (GitHub Settings > Developer settings >
   Personal access tokens > Tokens (classic)). Add it to your user-level NuGet configuration, never
   to a file in this repository; `Read-Host` keeps it off the command line:

   ```powershell
   $t = Read-Host 'token' -MaskInput
   dotnet nuget add source https://nuget.pkg.github.com/no1ross/index.json --name kaleido --username <your GitHub user> --password $t
   Remove-Variable t
   ```

   For the containers, set `KALEIDO_FEED_TOKEN` to the same token before `podman compose up --build`:
   the images restore with it as a build secret, never stored in an image. Building needs the .NET 9 SDK
   (`global.json`).

1. **Seed** the shared reference databases (once, and after reference data changes):

   ```powershell
   dotnet run --project src/Seeder/Kaleido.Samples.PriorAuth.Seeder.csproj -- --domains=ReferenceData,CodeSet,Configuration,ProviderSearch,MemberService
   ```

2. **Start** everything, either way:
   - **Containers** (Production settings): `podman compose up --build` in `compose/` runs the router
     (`http://localhost:8080`) and the services; the Aspire dashboard is at `http://localhost:18888`.
   - **Locally** (Development settings): `dotnet run` the router, the services and the EventCollector
     (projects under `src/Router/`, `src/Services/*/`, `src/EventCollector/`).
     For traces, `npx @microsoft/aspire-cli dashboard run`.

   Then run the UI, which is not containerized, either way: `npm start` in `ui/` (or Start on
   `ui/priorauth-ui.esproj` in Visual Studio), and open `http://localhost:4200`.

3. **Sign in** with a persona from the UI's persona picker (the router's dev login issues a signed
   token; nothing here is a real identity provider).

The databases are created at startup with `EnsureCreated`, which never changes an existing file:
after an entity change, delete the affected `.db` (see [`AGENTS.md`](AGENTS.md#local-run-workflow)).

## Personas: who can do what

| Persona | Roles | Can |
|---|---|---|
| `alice` | none | use Intake and the searches; is refused at the hand-off to Radiology |
| `bob` | `radiology` | run a request through Intake and Radiology to submission |
| `carol` | `admin`, `radiology` | the same as bob in this sample |

Every host enforces authorization (`ZeroTrust`): each service declares who may use it, and a
capability without a rule is not published at all. The registry is filtered per caller, so alice's
UI and agent never see Radiology's steps. When Intake hands a request to Radiology it calls **on
behalf of** the user (the same name and roles, plus the calling service as the actor), so Radiology
still knows it is bob; some capabilities accept only service-to-service calls. The rules are in
[`AGENTS.md`](AGENTS.md#auth-model).

## A request, end to end

Signed in as bob, in the UI or through an agent:

| # | Process | Step | What happens |
|---|---|---|---|
| 1 | Intake | Start | A process is created; its id travels on every later call, searches included. |
| 2 | Intake | Validate Member, Capture Member | The member comes from a lookup over the Member search (name, member ID, date of birth); the caller picks the match. |
| 3 | Intake | Capture Requested Service | The procedure code decides the product (Configuration's routing rules): an imaging code hands the request to **Radiology** with the same process id. The response names the target processor; the consumer reads Radiology's state next ([`docs/HANDOFF.md`](docs/HANDOFF.md)). |
| 4 | Radiology | Capture MRI Information, or Confirm CT Instead Of MRI | An **information request**: Radiology asks the questions configured for the service (some only when an earlier answer makes them apply). The UI shows a form; an agent asks the user round by round. |
| 5 | Radiology | Capture Requesting Provider, Capture Servicing Provider | Providers come from searches that only Radiology may call; Radiology publishes them to the caller as views over those internal searches. |
| 6 | Radiology | Capture Clinical Information | Clinical questions, when the service requires them. |
| 7 | Radiology | Review, Submit | A review of everything captured, then an attestation as the last information request. |

After every step that changes what a reviewer would see, the process records its snapshot in
**History** (the step fails if History does not record it). Each row in the Intake, Radiology and History databases also records who created and last changed it,
through which service, and when ([audit fields](AGENTS.md#audit-fields)).
The **History** page lists requests; a request can be resumed from there.

To follow one request across services, filter the Aspire dashboard by its process id or request id:
every hop carries both (correlation headers), and the events the services publish reach the
EventCollector with PHI removed.

## Connect an AI agent

The router's MCP endpoint is `http://localhost:8080/mcp`. It needs a persona's token:
`./scripts/mcp-login.ps1 bob` signs in and stores the token where the
repository's MCP configuration reads it. The agent starts with a short list of tools (the request
list and the entry steps); the next steps, their inputs and the searches they need arrive with each
response as the process moves on.

## More

- [`AGENTS.md`](AGENTS.md): conventions, auth model, history recording, audit fields, configuration per environment, ports
- [`docs/HANDOFF.md`](docs/HANDOFF.md): the Intake to Radiology handoff, its contract and the UI's handling
- [Kaleido](https://github.com/no1ross/Kaleido) and its [consumer guide](https://github.com/no1ross/Kaleido/blob/master/docs/guide/README.md): Kaleido's concepts with a smaller, tested example
- [MCP adapter](https://github.com/no1ross/Kaleido/blob/master/src/Kaleido.Http.Mcp/README.md): tool listing, information requests over MCP
