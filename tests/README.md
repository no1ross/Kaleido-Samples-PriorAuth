# Tests

```powershell
dotnet test Kaleido.Samples.PriorAuth.CI.slnf
```

runs both projects, as CI does (`.github/workflows/build.yml`). They also run from Visual Studio's Test
Explorer. Nothing needs to be running and nothing is read from `data/`: the functional tests seed
their own databases. The UI's tests live with the app (`ui/src/**/*.spec.ts`, `npm test` in `ui/`).

The tests use plain xUnit (v3) and Moq, with no coverage measurement.

| Project | Tests | How |
|---|---|---|
| `Kaleido.Samples.PriorAuth.UnitTests` | one class at a time: step handlers, services, translation of configured questionnaires, dev auth, the audit interceptor | the other services' answers are canned (`TestSupport/FakeKaleido`), databases are in-memory SQLite (`TestSupport/SqliteDatabase`) |
| `Kaleido.Samples.PriorAuth.FunctionalTests` | the sample as a caller sees it: a request from Intake through Radiology to submission, History, the queries, authorization, events, the router's login and MCP front door | the whole sample in-process (below) |

## The functional tests' hosts

`Hosting/PriorAuthHosts` (an assembly fixture, started once per run):

1. seeds the reference databases into a new temp folder with the seeder's own seeders;
2. starts every host (router, services, EventCollector) with `WebApplicationFactory`, in Development,
   on its usual port, with its databases in that folder;
3. routes every outgoing HTTP call to the in-process host on the request's port (`HostRouter`), so
   the hosts call each other as they do under `dotnet run`, credentials included. Only `localhost`
   is accepted: a container address in a Development setting fails the test.

`SampleClient` calls the services as the UI does in direct mode (one persona, the process id on every
call), and `PriorAuthFlow` takes a request's steps. Each test starts its own request, so tests do not
depend on each other.
