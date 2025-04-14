// --- Program.cs / Startup ---
// (No changes needed here, remains the same as previous version)

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddSingleton<IFeatureService, InMemoryFeatureService>();
builder.Services.AddScoped<IDataProvider, DummyDataProvider>();
builder.Services.AddScoped<ChangeMembershipEligibilityChecker>();

// Register Specifications
builder.Services.AddScoped<IsUserProfileActiveRule>();
builder.Services.AddScoped<HasValidSubscriptionAndPaymentRule>();
builder.Services.AddScoped<ExternalServiceACheckSpec>();
builder.Services.AddScoped<HasRecentActivitySpec>();
builder.Services.AddScoped<AlwaysTrueRule>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/checkEligibility/{userId}", async (
    Guid userId,
    ChangeMembershipEligibilityChecker checker,
    IsUserProfileActiveRule userActiveSpec,
    HasValidSubscriptionAndPaymentRule subAndPaySpec,
    ExternalServiceACheckSpec externalSpec,
    HasRecentActivitySpec activitySpec
) =>
{
    var subOrActivity = new OrChangeMembershipEligibilityRule(subAndPaySpec, activitySpec);
    var mainChecks = new AndChangeMembershipEligibilityRule(userActiveSpec, subOrActivity);
    var finalRule = new AndChangeMembershipEligibilityRule(mainChecks, externalSpec);

    var result = await checker.CheckAsync(userId, finalRule, CancellationToken.None);
    return Results.Ok(result);
});

app.Run();


[Flags]
public enum MembershipChangeDataDependency
{
    None = 0,
    UserProfile = 1 << 0,
    Subscription = 1 << 1,
    PaymentInfo = 1 << 2,
    ExternalServiceA = 1 << 3,
    ActivityHistory = 1 << 4
}

public interface IEligibilityCheckContext
{
    public UserProfile? UserProfile { get; }

    public Subscription? Subscription { get; }

    public PaymentInfo? PaymentInfo { get; }

    public string? ExternalServiceAResult { get; }

    public List<Activity>? ActivityHistory { get; }
}

public class EligibilityCheckContext : IEligibilityCheckContext
{
    public UserProfile? UserProfile { get; set; }
    public Subscription? Subscription { get; set; }
    public PaymentInfo? PaymentInfo { get; set; }
    public string? ExternalServiceAResult { get; set; }
    public List<Activity>? ActivityHistory { get; set; }

    public bool HasData(MembershipChangeDataDependency flag) => flag switch
    {
        MembershipChangeDataDependency.UserProfile => UserProfile != null,
        MembershipChangeDataDependency.Subscription => Subscription != null,
        MembershipChangeDataDependency.PaymentInfo => PaymentInfo != null,
        MembershipChangeDataDependency.ExternalServiceA => ExternalServiceAResult != null,
        MembershipChangeDataDependency.ActivityHistory => ActivityHistory != null,
        _ => false
    };
}

public class UserProfile
{
    public string Status { get; set; } = "Inactive";
}

public class Subscription
{
    public bool IsActive { get; set; } = false;
}

public class PaymentInfo
{
    public bool IsValid { get; set; } = false;
}

public class Activity
{
    public DateTime Timestamp { get; set; }
}

public interface IFeatureService
{
    bool IsEnabled(string featureName);
}

public class InMemoryFeatureService : IFeatureService
{
    /* ... implementation unchanged ... */
    private readonly Dictionary<string, bool> _features = new()
    {
        { "IsUserProfileActive", true },
        { "HasValidSubscriptionAndPayment", true },
        { "ExternalServiceACheck", true },
        { "HasRecentActivity", false },
        { "AlwaysTrue", true }
    };

    public bool IsEnabled(string featureName)
    {
        return _features.TryGetValue(featureName, out var isEnabled) && isEnabled;
    }
}

public interface IDataProvider
{
    Task<EligibilityCheckContext> FetchDataAsync(Guid userId, MembershipChangeDataDependency membershipChangeDataDependency, CancellationToken ct);
}

public class DummyDataProvider : IDataProvider
{
    /* ... implementation unchanged ... */
    public async Task<EligibilityCheckContext> FetchDataAsync(Guid userId, MembershipChangeDataDependency membershipChangeDataDependency,
        CancellationToken ct)
    {
        var context = new EligibilityCheckContext();
        var tasks = new List<Task>(); // Note: Tasks list wasn't used in prev Fetch logic, keep for structure

        // Simulate fetching ONLY required data
        if (membershipChangeDataDependency.HasFlag(MembershipChangeDataDependency.UserProfile))
        {
            await Task.Delay(50, ct);
            context.UserProfile = new UserProfile { Status = "Active" };
        }

        if (membershipChangeDataDependency.HasFlag(MembershipChangeDataDependency.Subscription))
        {
            await Task.Delay(30, ct);
            context.Subscription = new Subscription { IsActive = true };
        }

        if (membershipChangeDataDependency.HasFlag(MembershipChangeDataDependency.PaymentInfo))
        {
            await Task.Delay(70, ct);
            context.PaymentInfo = new PaymentInfo { IsValid = true };
        }

        if (membershipChangeDataDependency.HasFlag(MembershipChangeDataDependency.ExternalServiceA))
        {
            await Task.Delay(100, ct);
            context.ExternalServiceAResult = "Success";
        }

        if (membershipChangeDataDependency.HasFlag(MembershipChangeDataDependency.ActivityHistory))
        {
            await Task.Delay(60, ct);
            context.ActivityHistory = [new Activity { Timestamp = DateTime.UtcNow.AddDays(-5) }]; // Collection initializer
        }

        // await Task.WhenAll(tasks); // Keep commented unless tasks list is populated
        return context;
    }
}

public interface IControlledFeature
{
    bool IsEnabled { get; }
}

public interface IDependencyDeclaration<out TDependencies>
{
    TDependencies GetDependencies();
}

public interface IEligibilityViolation
{
    bool IsFromRule(IControlledChangeMembershipEligibilityRule rule);

    bool IsFromRule(Type ruleType);

    Type RuleType();
}

public interface IEligibilityRuleResult
{
    bool HasViolation { get; }

    IEligibilityViolation? Violation { get; }
}

public class EligibilityRuleResult : IEligibilityRuleResult
{
    public bool HasViolation => Violation != null;

    public IEligibilityViolation? Violation { get; }

    private EligibilityRuleResult(IEligibilityViolation? violation)
    {
        Violation = violation;
    }

    public static IEligibilityRuleResult Pass()
    {
        return new EligibilityRuleResult(null);
    }

    public static IEligibilityRuleResult Fail(IEligibilityViolation violation)
    {
        return new EligibilityRuleResult(violation);
    }
}

public class EligibilityViolation(IControlledChangeMembershipEligibilityRule rule) : IEligibilityViolation
{
    public bool IsFromRule(IControlledChangeMembershipEligibilityRule specificRule)
    {
        return IsFromRule(rule.GetType());
    }

    public bool IsFromRule(Type ruleType)
    {
        return ruleType.IsInstanceOfType(rule);
    }

    public Type RuleType()
    {
        return rule.GetType();
    }
}

public interface IEligibilityRule<out TDependencies, in TContext> : IDependencyDeclaration<TDependencies>
{
    ValueTask<IEligibilityRuleResult> IsEligibleAsync(TContext context, CancellationToken ct);
}

public interface IChangeMembershipEligibilityRule : IEligibilityRule<MembershipChangeDataDependency, IEligibilityCheckContext>;


public interface IControlledChangeMembershipEligibilityRule : IChangeMembershipEligibilityRule, IControlledFeature;


public abstract class ControlledChangeMembershipEligibilityBaseRule<TRule>(IFeatureService featureService)
    : IControlledChangeMembershipEligibilityRule
{
    public virtual bool IsEnabled => featureService.IsEnabled(typeof(TRule).Name);

    public abstract MembershipChangeDataDependency GetDependencies();
    public abstract ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct);
}

public class IsUserProfileActiveRule(IFeatureService featureService)
    : ControlledChangeMembershipEligibilityBaseRule<IsUserProfileActiveRule>(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => IsEnabled
        ? MembershipChangeDataDependency.UserProfile
        : MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsEnabled) return ValueTask.FromResult(EligibilityRuleResult.Pass());

        var result = context.UserProfile?.Status == "Active"
            ? EligibilityRuleResult.Pass()
            : EligibilityRuleResult.Fail(new EligibilityViolation(this));

        return new ValueTask<IEligibilityRuleResult>(result);
    }
}

public class HasValidSubscriptionAndPaymentRule(IFeatureService featureService)
    : ControlledChangeMembershipEligibilityBaseRule<HasValidSubscriptionAndPaymentRule>(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => IsEnabled
        ? MembershipChangeDataDependency.Subscription | MembershipChangeDataDependency.PaymentInfo
        : MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsEnabled) return ValueTask.FromResult(EligibilityRuleResult.Pass());

        var result = (context.Subscription?.IsActive ?? false) && (context.PaymentInfo?.IsValid ?? false)
            ? EligibilityRuleResult.Pass()
            : EligibilityRuleResult.Fail(new EligibilityViolation(this));

        return ValueTask.FromResult(result);
    }
}

// Using primary constructor for injected dependency
public class ExternalServiceACheckSpec(IFeatureService featureService)
    : ControlledChangeMembershipEligibilityBaseRule<ExternalServiceACheckSpec>(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => IsEnabled
        ? MembershipChangeDataDependency.ExternalServiceA
        : MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsEnabled) return ValueTask.FromResult(EligibilityRuleResult.Pass());

        var result = context.ExternalServiceAResult == "Success"
            ? EligibilityRuleResult.Pass()
            : EligibilityRuleResult.Fail(new EligibilityViolation(this));

        return ValueTask.FromResult(result);
    }
}

public class HasRecentActivitySpec(IFeatureService featureService)
    : ControlledChangeMembershipEligibilityBaseRule<HasRecentActivitySpec>(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => IsEnabled
        ? MembershipChangeDataDependency.ActivityHistory
        : MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsEnabled) return ValueTask.FromResult(EligibilityRuleResult.Pass());

        var recentDate = DateTime.UtcNow.AddDays(-7);
        var result = context.ActivityHistory?.Any(a => a.Timestamp > recentDate) ?? false
            ? EligibilityRuleResult.Pass()
            : EligibilityRuleResult.Fail(new EligibilityViolation(this));

        return ValueTask.FromResult(result);
    }
}

public class AlwaysTrueRule(IFeatureService featureService) : ControlledChangeMembershipEligibilityBaseRule<AlwaysTrueRule>(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        return ValueTask.FromResult(EligibilityRuleResult.Pass());
    }
}

public abstract class ChangeMembershipEligibilityBaseRule<TLeftRule, TRightRule>(TLeftRule leftRule, TRightRule rightRule)
    : IChangeMembershipEligibilityRule
    where TLeftRule : IChangeMembershipEligibilityRule
    where TRightRule : IChangeMembershipEligibilityRule
{
    protected TLeftRule LeftRule = leftRule;
    protected TRightRule RightRule = rightRule;

    public MembershipChangeDataDependency GetDependencies()
    {
        return LeftRule.GetDependencies() | RightRule.GetDependencies();
    }

    public abstract ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct);
}

public class AndChangeMembershipEligibilityRule(IChangeMembershipEligibilityRule leftRule, IChangeMembershipEligibilityRule rightRule)
    : ChangeMembershipEligibilityBaseRule<IChangeMembershipEligibilityRule, IChangeMembershipEligibilityRule>(leftRule, rightRule)
{
    public override async ValueTask<IEligibilityRuleResult> IsEligibleAsync(
        IEligibilityCheckContext context,
        CancellationToken ct)
    {
        var leftRuleResult = await LeftRule.IsEligibleAsync(context, ct);
        if (leftRuleResult.HasViolation) return leftRuleResult;

        var rightRuleResult = await RightRule.IsEligibleAsync(context, ct);
        return rightRuleResult;
    }
}

public class OrChangeMembershipEligibilityRule(IChangeMembershipEligibilityRule leftRule, IChangeMembershipEligibilityRule rightRule)
    : ChangeMembershipEligibilityBaseRule<IChangeMembershipEligibilityRule, IChangeMembershipEligibilityRule>(leftRule, rightRule)
{
    public override async ValueTask<IEligibilityRuleResult> IsEligibleAsync(
        IEligibilityCheckContext context,
        CancellationToken ct)
    {
        var leftRuleResult = await LeftRule.IsEligibleAsync(context, ct);
        if (!leftRuleResult.HasViolation) return leftRuleResult;

        var rightRuleResult = await RightRule.IsEligibleAsync(context, ct);
        return rightRuleResult;
    }
}

public class ChangeMembershipEligibilityChecker(IDataProvider dataProvider, IFeatureService featureService)
{
    public async ValueTask<bool> CheckAsync(Guid userId, IControlledChangeMembershipEligibilityRule rule, CancellationToken ct)
    {
        var requiredData = rule.GetDependencies();

        var dataContext = await dataProvider.FetchDataAsync(userId, requiredData, ct);
        var result = await rule.IsEligibleAsync(dataContext, ct);

        return result.HasViolation;
    }

    public async ValueTask<bool> CheckAsync(Guid userId, IChangeMembershipEligibilityRule rule, CancellationToken ct)
    {
        var requiredData = rule.GetDependencies();

        var dataContext = await dataProvider.FetchDataAsync(userId, requiredData, ct);
        var results = await rule.IsEligibleAsync(dataContext, ct);

        return results.HasViolation;
    }
}