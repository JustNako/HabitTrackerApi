using HabitTrackerApi.Data;
using HabitTrackerApi.DTOs;
using HabitTrackerApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApi.Services;

public class FriendService
{
    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public FriendService(AppDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // Get your invite code
    public async Task<string?> GetMyInviteCode(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user?.InviteCode;
    }

    // Send a friend request via invite code
    public async Task<(bool Success, string Error)> SendRequest(string requesterId, string inviteCode)
    {
        var addressee = await _userManager.Users
            .FirstOrDefaultAsync(u => u.InviteCode == inviteCode);

        if (addressee == null)
            return (false, "No user found with that invite code.");

        if (addressee.Id == requesterId)
            return (false, "You cannot add yourself.");

        var existing = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == addressee.Id) ||
            (f.RequesterId == addressee.Id && f.AddresseeId == requesterId));

        if (existing != null)
            return (false, existing.Status == FriendshipStatus.Accepted
                ? "You are already friends."
                : "A friend request already exists.");

        _context.Friendships.Add(new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = addressee.Id
        });
        await _context.SaveChangesAsync();
        return (true, string.Empty);
    }

    // Accept a pending request
    public async Task<(bool Success, string Error)> AcceptRequest(string userId, Guid friendshipId)
    {
        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId && f.AddresseeId == userId && f.Status == FriendshipStatus.Pending);

        if (friendship == null)
            return (false, "Friend request not found.");

        friendship.Status = FriendshipStatus.Accepted;
        await _context.SaveChangesAsync();
        return (true, string.Empty);
    }

    // Remove a friend or decline a request
    public async Task<(bool Success, string Error)> RemoveFriend(string userId, Guid friendshipId)
    {
        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f => f.Id == friendshipId &&
                (f.RequesterId == userId || f.AddresseeId == userId));

        if (friendship == null)
            return (false, "Friendship not found.");

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync();
        return (true, string.Empty);
    }

    // Get accepted friends with their public habit data
    public async Task<List<FriendDto>> GetFriends(string userId)
    {
        var friendships = await _context.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId) &&
                        f.Status == FriendshipStatus.Accepted)
            .ToListAsync();

        var result = new List<FriendDto>();
        var weekStart = DateTime.UtcNow.Date.AddDays(-7);

        foreach (var f in friendships)
        {
            var friendId = f.RequesterId == userId ? f.AddresseeId : f.RequesterId;
            var friendUser = await _userManager.FindByIdAsync(friendId);
            if (friendUser == null) continue;

            var habits = await _context.Habits
                .Include(h => h.Logs.Where(l => l.Date >= weekStart))
                .Where(h => h.UserId == friendId && h.IsPublic)
                .ToListAsync();

            var publicHabits = habits.Select(h => new PublicHabitDto
            {
                Title = h.Title,
                CompletedThisWeek = h.Logs.Count(l => l.IsCompleted),
                CurrentStreak = CalculateStreak(h.Logs.OrderByDescending(l => l.Date).ToList())
            }).ToList();

            result.Add(new FriendDto
            {
                Id = friendId,
                UserName = friendUser.UserName ?? "Unknown",
                IsPro = friendUser.IsPro,
                TotalPublicHabits = publicHabits.Count,
                GlobalStreak = publicHabits.Any() ? publicHabits.Max(h => h.CurrentStreak) : 0,
                PublicHabits = publicHabits
            });
        }

        return result;
    }

    // Get pending incoming requests
    public async Task<List<FriendRequestDto>> GetPendingRequests(string userId)
    {
        return await _context.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
            .Select(f => new FriendRequestDto
            {
                FriendshipId = f.Id,
                RequesterId = f.RequesterId,
                RequesterName = f.Requester.UserName ?? "Unknown",
                SentAt = f.CreatedAt
            })
            .ToListAsync();
    }

    private static int CalculateStreak(List<HabitLog> logs)
    {
        var streak = 0;
        var date = DateTime.UtcNow.Date;
        foreach (var log in logs)
        {
            if (log.Date.Date == date && log.IsCompleted)
            {
                streak++;
                date = date.AddDays(-1);
            }
            else break;
        }
        return streak;
    }
}
