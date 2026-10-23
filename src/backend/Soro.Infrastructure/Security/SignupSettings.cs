using Soro.Application.Signup;

namespace Soro.Infrastructure.Security;

public sealed record SignupSettings(string CurrentTermsVersion, int TrialDays) : ISignupSettings;
