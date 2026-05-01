namespace HabitTrackerApi.Entities;

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RequesterId { get; set; } = null!;
    public ApplicationUser Requester { get; set; } = null!;
    public string AddresseeId { get; set; } = null!;
    public ApplicationUser Addressee { get; set; } = null!;
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum FriendshipStatus
{
    Pending,
    Accepted
}