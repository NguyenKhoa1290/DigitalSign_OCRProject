namespace HauDocumentApp.Models;

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword     { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string? Email          { get; set; }
    public string? EmailVerificationOtp { get; set; }
    public string? PhoneNumber    { get; set; }
}

public class SendEmailVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Email           { get; set; } = string.Empty;
    public string Otp             { get; set; } = string.Empty;
    public string NewPassword     { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
