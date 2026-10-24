using Kaleido.Http.Processor;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.Contexts;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Parameters;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.History.Process.Steps;
using Kaleido.Samples.PriorAuth.Member.Queryable.ViewSources.Parameters;
using Kaleido.Samples.PriorAuth.Member.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Questionnaires;
using Kaleido.Samples.PriorAuth.Radiology.Data;
using Kaleido.Samples.PriorAuth.Radiology.Data.Entities;
using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kaleido.Samples.PriorAuth.UnitTests.Radiology;

/// <summary>
/// Radiology's database and the other services' canned answers: members, modality rules (MRI
/// 73721-73723, CT 70450-70486), questionnaires by step name, MRI code rules, and History.
/// </summary>
public sealed class RadiologyTestBed : IDisposable
{
    public SqliteDatabase<RadiologyDbContext> Database { get; } = new(options => new RadiologyDbContext(options));

    public FakeKaleido Kaleido { get; } = new();

    public List<MemberDetailsView> Members { get; } = [];

    /// <summary>Questionnaires the Configuration service selects, keyed by step name.</summary>
    public Dictionary<string, QuestionnaireDefinitionView> QuestionnairesByStep { get; } = [];

    public List<MriProcedureCodeRuleQueryContext> MriRules { get; } = [];

    public StepExecutionOutcome HistoryOutcome { get; set; } = StepExecutionOutcome.Completed;

    public ProcessStepContext Context { get; } = Steps.Context();

    public RadiologyTestBed()
    {
        Kaleido
            .View<MemberDetailsViewParameters, MemberDetailsView>("Member", "MemberQueryContextSource", "MemberDetailsViewSource",
                parameters => Members.Where(x => x.MemberId == parameters?.MemberId && x.MemberEnrollmentId == parameters?.MemberEnrollmentId))
            .Source(
                "Configuration",
                "ProcedureModalityRuleQueryContextSource",
                new ProcedureModalityRuleQueryContext { CodeSystem = ProcedureCodeSystem.Cpt, CodeRangeStart = 73721, CodeRangeEnd = 73723, Modality = ProcedureModality.Mri },
                new ProcedureModalityRuleQueryContext { CodeSystem = ProcedureCodeSystem.Cpt, CodeRangeStart = 70450, CodeRangeEnd = 70486, Modality = ProcedureModality.Ct })
            .View<QuestionnaireDefinitionViewParameters, QuestionnaireDefinitionView>("Configuration", "QuestionnaireDefinitionQueryContextSource", "QuestionnaireDefinitionViewSource",
                parameters => QuestionnairesByStep.TryGetValue(parameters?.StepName ?? string.Empty, out var found) ? [found] : [])
            .Source<MriProcedureCodeRuleQueryContext>("Configuration", "MriProcedureCodeRuleQueryContextSource", _ => MriRules)
            .Step<UpsertPriorAuthRecordStep>("History", _ => FakeKaleido.Answer(nameof(UpsertPriorAuthRecordStep), HistoryOutcome));
    }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public RadiologyDbContext Db => Database.Context;

    public ConfiguredInformationRequests InformationRequests() => new(new ConfiguredQuestionnaireClient(Kaleido), Db);

    public HistoryClient HistoryClient() => new(Db, Kaleido, NullLogger<HistoryClient>.Instance);

    public MemberEligibilityService Eligibility() =>
        new(Db, new MemberDetailsClient(Kaleido), new ProcedureModalityClient(Kaleido), InformationRequests());

    public MemberDetailsView AddMember(DateOnly? effective = null, DateOnly? terminated = null)
    {
        var member = new MemberDetailsView
        {
            MemberId = Guid.NewGuid(),
            MemberEnrollmentId = Guid.NewGuid(),
            MemberNumber = "M-200",
            DisplayName = "John Roe",
            PlanId = "PLAN-1",
            PlanName = "Gold",
            LineOfBusiness = LineOfBusiness.Commercial,
            EffectiveDate = effective ?? new DateOnly(2025, 1, 1),
            TerminationDate = terminated
        };

        Members.Add(member);
        return member;
    }

    /// <summary>A recorded prior authorization for this process, with a member and the given services.</summary>
    public async Task<PriorAuthorization> AddPriorAuthorizationAsync(string? statedDetailsJson = null, params string[] codeValues)
    {
        var priorAuthorization = new PriorAuthorization
        {
            PriorAuthorizationId = Guid.NewGuid(),
            ProcessId = Context.ProcessId,
            Status = PriorAuthorizationStatus.Draft,
            DateOfService = new DateOnly(2026, 11, 2),
            Member = new PriorAuthorizationMember { MemberNumber = "M-200", DisplayName = "John Roe", PlanId = "PLAN-1", LineOfBusiness = LineOfBusiness.Commercial }
        };

        foreach (var codeValue in codeValues)
        {
            priorAuthorization.RequestedServices.Add(new PriorAuthorizationRequestedService
            {
                PriorAuthorizationRequestedServiceId = Guid.NewGuid(),
                UserEnteredCodeValue = codeValue,
                UserEnteredCodeSystem = ProcedureCodeSystem.Cpt,
                ResolvedCodeValue = codeValue,
                ResolvedCodeSystem = ProcedureCodeSystem.Cpt,
                Description = $"Service {codeValue}",
                StatedDetailsJson = statedDetailsJson
            });
        }

        Db.PriorAuthorizations.Add(priorAuthorization);
        await Db.SaveChangesAsync(Token);
        return priorAuthorization;
    }

    public void Dispose() => Database.Dispose();
}
