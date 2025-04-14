// --- Program.cs / Startup ---
// (No changes needed here, remains the same as previous version)

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddSingleton<IFeatureService, InMemoryFeatureService>();
builder.Services.AddScoped<IDataProvider, DummyDataProvider>();
builder.Services.AddScoped<EligibilityChecker>();

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
    EligibilityChecker checker,
    IsUserProfileActiveRule userActiveSpec,
    HasValidSubscriptionAndPaymentRule subAndPaySpec,
    ExternalServiceACheckSpec externalSpec,
    HasRecentActivitySpec activitySpec,
    IFeatureService featureService
) =>
{
    // Constructing the composite rule - using primary constructors implicitly
    var subOrActivity = new OrRule(featureService, subAndPaySpec, activitySpec);
    var mainChecks = new AndRule(featureService, userActiveSpec, subOrActivity);
    var finalRule = new AndRule(featureService, mainChecks, externalSpec);

    var result = await checker.CheckAsync(userId, finalRule, CancellationToken.None);
    return Results.Ok(result);
});

app.Run();


// --- Data Dependencies Enum & Context ---
// (No changes needed here)
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

// Dummy classes for context properties (unchanged)
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


// --- Feature Service & Data Provider Interfaces/Implementations ---
// (No changes needed here)
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
    bool IsFromRule(IChangeMembershipEligibilityRule rule);

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

    protected EligibilityRuleResult(IEligibilityViolation? violation)
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

public class EligibilityViolation(IChangeMembershipEligibilityRule rule) : IEligibilityViolation
{
    public bool IsFromRule(IChangeMembershipEligibilityRule rule)
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

public interface IEligibilityRule<out TDependencies, in TContext> : IControlledFeature, IDependencyDeclaration<TDependencies>
{
    ValueTask<IEligibilityRuleResult> IsEligibleAsync(TContext context, CancellationToken ct);
}

public interface IChangeMembershipEligibilityRule : IEligibilityRule<MembershipChangeDataDependency, IEligibilityCheckContext>;


public abstract class ChangeMembershipEligibilityBaseRule<TRule>(IFeatureService featureService) : IChangeMembershipEligibilityRule
{
    public virtual bool IsEnabled => featureService.IsEnabled(typeof(TRule).Name);

    public abstract MembershipChangeDataDependency GetDependencies();
    public abstract ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct);
}

public class IsUserProfileActiveRule(IFeatureService featureService)
    : ChangeMembershipEligibilityBaseRule<IsUserProfileActiveRule>(featureService)
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
    : ChangeMembershipEligibilityBaseRule<HasValidSubscriptionAndPaymentRule>(featureService)
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
    : ChangeMembershipEligibilityBaseRule<ExternalServiceACheckSpec>(featureService)
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
    : ChangeMembershipEligibilityBaseRule<HasRecentActivitySpec>(featureService)
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

public class AlwaysTrueRule(IFeatureService featureService) : ChangeMembershipEligibilityBaseRule<AlwaysTrueRule(featureService)
{
    public override MembershipChangeDataDependency GetDependencies() => MembershipChangeDataDependency.None;

    public override ValueTask<IEligibilityRuleResult> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        return ValueTask.FromResult(EligibilityRuleResult.Pass());
    }
}


public abstract class CompositeRule(IFeatureService featureService, params IEligibilityRule[] children)
    : ChangeMembershipEligibilityBaseRule 
{
    protected readonly IEligibilityRule[] Children = children ?? throw new ArgumentNullException(nameof(children));
}

// Primary constructor used here too
public class AndRule(IFeatureService featureService, IEligibilityRule left, IEligibilityRule right)
    : CompositeRule(featureService, left, right)
{
    public override string Name => $"And({Children[0].Name},{Children[1].Name})"; // Example override
    public override bool IsActive(IFeatureService fs) => Children[0].IsActive(fs) && Children[1].IsActive(fs);

    public override MembershipChangeDataDependency GetDependencies(IFeatureService fs)
    {
        if (!IsActive(fs)) return MembershipChangeDataDependency.None;
        // Aggregate active children dependencies
        return Children[0].GetDependencies(fs) | Children[1].GetDependencies(fs);
    }

    public override async Task<bool> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsActive(featureService)) return false; // Use captured featureService
        // Short-circuit
        return await Children[0].IsEligibleAsync(context, ct) && await Children[1].IsEligibleAsync(context, ct);
    }
}

public class OrRule(IFeatureService featureService, IEligibilityRule left, IEligibilityRule right)
    : CompositeRule(featureService, left, right)
{
    public override string Name => $"Or({Children[0].Name},{Children[1].Name})";
    public override bool IsActive(IFeatureService fs) => Children[0].IsActive(fs) || Children[1].IsActive(fs);

    public override MembershipChangeDataDependency GetDependencies(IFeatureService fs)
    {
        if (!IsActive(fs)) return MembershipChangeDataDependency.None;
        MembershipChangeDataDependency aggregated = MembershipChangeDataDependency.None;
        // Must check each child individually as one might be active while the other isn't
        if (Children[0].IsActive(fs)) aggregated |= Children[0].GetDependencies(fs);
        if (Children[1].IsActive(fs)) aggregated |= Children[1].GetDependencies(fs);
        return aggregated;
    }

    public override async Task<bool> IsEligibleAsync(EligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsActive(featureService)) return false;

        bool leftIsActive = Children[0].IsActive(featureService);
        bool rightIsActive = Children[1].IsActive(featureService);

        // Short-circuit if left is active and satisfies
        if (leftIsActive && await Children[0].IsEligibleAsync(context, ct)) return true;
        // Evaluate right only if it's active (and left didn't satisfy)
        if (rightIsActive) return await Children[1].IsEligibleAsync(context, ct);

        return false; // Neither active child satisfied
    }
}

// Primary constructor captures dependencies
public class NotRule(IFeatureService featureService, IEligibilityRule operand)
    : ChangeMembershipEligibilityBaseRule // Not technically composite, inherits Base directly
{
    public override string Name => $"Not({operand.Name})";

    public override bool IsActive(IFeatureService fs) => operand.IsActive(fs);

    public override MembershipChangeDataDependency GetDependencies(IFeatureService fs)
        => IsActive(fs) ? operand.GetDependencies(fs) : MembershipChangeDataDependency.None;

    public override async Task<bool> IsEligibleAsync(IEligibilityCheckContext context, CancellationToken ct)
    {
        if (!IsActive(featureService)) return false; // Use captured featureService
        return !await operand.IsEligibleAsync(context, ct);
    }
}


// --- REFACTORED Eligibility Checker ---
// Uses primary constructor for dependencies
public class EligibilityChecker(IDataProvider dataProvider, IFeatureService featureService)
{
    public async Task<bool> CheckAsync(Guid userId, IEligibilityRule rootRule, CancellationToken ct)
    {
        if (!rootRule.IsActive(featureService))
        {
            return false;
        }

        var requiredData = rootRule.GetDependencies(featureService);
        if (requiredData == MembershipChangeDataDependency.None && !(rootRule is AlwaysTrueRule)) // Small optimization: Fetch only if needed
        {
            Console.WriteLine("No data dependencies identified, skipping fetch.");
            // Execute without fetching if no data needed (handle AlwaysTrueRule potentially)
            return await rootRule.IsEligibleAsync(new EligibilityCheckContext(), ct); // Pass empty context
        }

        var dataContext = await dataProvider.FetchDataAsync(userId, requiredData, ct);
        return await rootRule.IsEligibleAsync(dataContext, ct);
    }
}