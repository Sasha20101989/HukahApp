using HookahPlatform.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HookahPlatform.UserService.Persistence;

public sealed class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<PermissionEntity> Permissions => Set<PermissionEntity>();
    public DbSet<RolePermissionEntity> RolePermissions => Set<RolePermissionEntity>();
    public DbSet<StaffShiftEntity> StaffShifts => Set<StaffShiftEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureIntegrationOutbox();
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.RoleId).HasColumnName("role_id");
            entity.Property(user => user.BranchId).HasColumnName("branch_id");
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
        });
        modelBuilder.Entity<RoleEntity>().ToTable("roles").HasKey(role => role.Id);
        modelBuilder.Entity<PermissionEntity>().ToTable("permissions").HasKey(permission => permission.Id);
        modelBuilder.Entity<RolePermissionEntity>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(value => new { value.RoleId, value.PermissionId });
            entity.Property(value => value.RoleId).HasColumnName("role_id");
            entity.Property(value => value.PermissionId).HasColumnName("permission_id");
        });
        modelBuilder.Entity<StaffShiftEntity>(entity =>
        {
            entity.ToTable("staff_shifts");
            entity.HasKey(shift => shift.Id);
            entity.Property(shift => shift.StaffId).HasColumnName("staff_id");
            entity.Property(shift => shift.BranchId).HasColumnName("branch_id");
            entity.Property(shift => shift.StartsAt).HasColumnName("starts_at");
            entity.Property(shift => shift.EndsAt).HasColumnName("ends_at");
            entity.Property(shift => shift.ActualStartedAt).HasColumnName("actual_started_at");
            entity.Property(shift => shift.ActualFinishedAt).HasColumnName("actual_finished_at");
            entity.Property(shift => shift.RoleOnShift).HasColumnName("role_on_shift");
            entity.Property(shift => shift.CancelReason).HasColumnName("cancel_reason");
        });
    }
}

public sealed class UserEntity
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid? BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RoleEntity { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string Code { get; set; } = string.Empty; }
public sealed class PermissionEntity { public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string? Description { get; set; } }
public sealed class RolePermissionEntity { public Guid RoleId { get; set; } public Guid PermissionId { get; set; } }
public sealed class StaffShiftEntity
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ActualStartedAt { get; set; }
    public DateTimeOffset? ActualFinishedAt { get; set; }
    public string? RoleOnShift { get; set; }
    public string? CancelReason { get; set; }
}
