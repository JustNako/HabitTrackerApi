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
    private readonly NotificationService _notifications;
    private readonly AchievementEvaluatorService _achievements;

    public FriendService(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        NotificationService notifications,
        AchievementEvaluatorService achievements)
    {
        _context = context;
        _userManager = userManager;
        _notifications = notifications;
        _achievements = achievements;
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

        // 🔔 Notify the target user about the incoming friend request
        var requester = await _userManager.FindByIdAsync(requesterId);
        if (addressee.FcmToken != null && requester != null)
        {
            await _notifications.SendAsync(
                addressee.FcmToken,
                title: "New Friend Request",
                body: $"{requester.UserName} wants to be your friend on Flow!",
                data: new Dictionary<string, string> { ["type"] = "friend_request" }
            );
        }

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

        // 🔔 Notify the original requester that their request was accepted
        var requester = await _userManager.FindByIdAsync(friendship.RequesterId);
        var accepter = await _userManager.FindByIdAsync(userId);
        if (requester?.FcmToken != null && accepter != null)
        {
            await _notifications.SendAsync(
                requester.FcmToken,
                title: "Friend Request Accepted!",
                body: $"{accepter.UserName} accepted your friend request. You can now see each other's habits!",
                data: new Dictionary<string, string> { ["type"] = "request_accepted" }
            );
        }

        // Evaluate achievements for both users
        await _achievements.EvaluateUserAsync(userId);
        await _achievements.EvaluateUserAsync(friendship.RequesterId);

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

        foreach (var f in friendships)
        {
            var friendId = f.RequesterId == userId ? f.AddresseeId : f.RequesterId;
            var friendUser = await _userManager.FindByIdAsync(friendId);
            if (friendUser == null) continue;

            var friendOffset = friendUser.TimeZoneOffset;
            var weekStart = DateTime.UtcNow.AddMinutes(friendOffset).Date.AddDays(-7);

            var habits = await _context.Habits
                .Include(h => h.Logs)
                .Where(h => h.UserId == friendId && h.IsPublic)
                .ToListAsync();

            var publicHabits = habits.Select(h => new PublicHabitDto
            {
                Title = h.Title,
                CompletedThisWeek = h.Logs.Count(l => l.IsCompleted && l.Date >= weekStart),
                CurrentStreak = CalculateStreak(h.Logs.OrderByDescending(l => l.Date).ToList(), friendOffset)
            }).ToList();

            result.Add(new FriendDto
            {
                FriendshipId = f.Id,
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

    // Get list of suggested users
    public async Task<List<SuggestedUserDto>> GetSuggestions(string userId, int limit = 10)
    {
        // Get all user IDs involved in friendships with the current user (either requester or addressee)
        var connectedUserIds = await _context.Friendships
            .Where(f => f.RequesterId == userId || f.AddresseeId == userId)
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

        // Also exclude the current user themselves
        connectedUserIds.Add(userId);

        // Fetch suggested users (excluding connected ones)
        var suggestions = await _userManager.Users
            .Where(u => !connectedUserIds.Contains(u.Id))
            .OrderByDescending(u => u.JoinedAt) // Suggest newer/recently active users first
            .Take(limit)
            .Select(u => new SuggestedUserDto
            {
                Id = u.Id,
                UserName = u.UserName ?? "Unknown",
                IsPro = u.IsPro
            })
            .ToListAsync();

        return suggestions;
    }

    // Send a friend request using user's Id directly
    public async Task<(bool Success, string Error)> SendRequestById(string requesterId, string addresseeId)
    {
        if (requesterId == addresseeId)
            return (false, "You cannot add yourself.");

        var addressee = await _userManager.FindByIdAsync(addresseeId);
        if (addressee == null)
            return (false, "User not found.");

        var existing = await _context.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == addresseeId) ||
            (f.RequesterId == addresseeId && f.AddresseeId == requesterId));

        if (existing != null)
            return (false, existing.Status == FriendshipStatus.Accepted
                ? "You are already friends."
                : "A friend request already exists.");

        _context.Friendships.Add(new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = addresseeId
        });
        await _context.SaveChangesAsync();

        // 🔔 Notify the target user about the incoming friend request
        var requester = await _userManager.FindByIdAsync(requesterId);
        if (addressee.FcmToken != null && requester != null)
        {
            await _notifications.SendAsync(
                addressee.FcmToken,
                title: "New Friend Request",
                body: $"{requester.UserName} wants to be your friend on Flow!",
                data: new Dictionary<string, string> { ["type"] = "friend_request" }
            );
        }

        return (true, string.Empty);
    }

    private static int CalculateStreak(List<HabitLog> logs, int timeZoneOffset)
    {
        var logsDates = logs.Where(l => l.IsCompleted)
                            .Select(l => l.Date.Date)
                            .Distinct()
                            .OrderByDescending(d => d)
                            .ToList();

        if (!logsDates.Any()) return 0;

        var today = DateTime.UtcNow.AddMinutes(timeZoneOffset).Date;
        var todayCompleted = logsDates.Contains(today);
        var expected = todayCompleted ? today : today.AddDays(-1);

        int streak = 0;
        foreach (var logDate in logsDates)
        {
            if (logDate == expected)
            {
                streak++;
                expected = expected.AddDays(-1);
            }
            else if (logDate < expected)
            {
                break;
            }
        }
        return streak;
    }
}
