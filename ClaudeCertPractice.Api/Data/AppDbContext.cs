using ClaudeCertPractice.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCertPractice.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ExamResultEntity> ExamResults => Set<ExamResultEntity>();
    public DbSet<ResultQuestionEntity> ResultQuestions => Set<ResultQuestionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(320);
            entity.Property(u => u.Name).HasMaxLength(200);
            entity.Property(u => u.Role).HasMaxLength(32);
        });

        modelBuilder.Entity<ExamResultEntity>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasMaxLength(64);
            entity.Property(r => r.SessionId).HasMaxLength(64);
            entity.Property(r => r.SourceMode).HasMaxLength(32);
            entity.HasOne(r => r.User)
                .WithMany(u => u.Results)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => r.CompletedAt);
        });

        modelBuilder.Entity<ResultQuestionEntity>(entity =>
        {
            entity.Property(q => q.ExamResultId).HasMaxLength(64);
            entity.Property(q => q.Options).HasColumnType("jsonb");
            entity.HasOne(q => q.ExamResult)
                .WithMany(r => r.Questions)
                .HasForeignKey(q => q.ExamResultId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
