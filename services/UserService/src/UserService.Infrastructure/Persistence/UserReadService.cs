using Microsoft.EntityFrameworkCore;
using UserService.Application.Abstractions;
using UserService.Application.Users;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Persistence;

public sealed class UserReadService(UserDbContext dbContext) : IUserReadService
{
    public async Task<UserResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(
                u.Id,
                u.Email.Value,
                u.DisplayName,
                u.Role.ToString(),
                u.IsActive,
                u.LastLoginAtUtc,
                u.CreatedAtUtc,
                u.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResponse<UserResponse>> ListAsync(
        Role? role,
        bool? isActive,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking().AsQueryable();

        if (role.HasValue)
        {
            query = query.Where(u => u.Role == role.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Email.Value, pattern) ||
                EF.Functions.ILike(u.DisplayName, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserResponse(
                u.Id,
                u.Email.Value,
                u.DisplayName,
                u.Role.ToString(),
                u.IsActive,
                u.LastLoginAtUtc,
                u.CreatedAtUtc,
                u.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<UserResponse>(
            items,
            page,
            pageSize,
            totalCount,
            totalPages);
    }
}
