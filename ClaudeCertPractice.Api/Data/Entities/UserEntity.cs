namespace ClaudeCertPractice.Api.Data.Entities;

public class UserEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = UserRoles.User;
    public string? PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<ExamResultEntity> Results { get; set; } = [];
}

public static class UserRoles
{
    public const string User = "User";
    public const string Admin = "Admin";
}
