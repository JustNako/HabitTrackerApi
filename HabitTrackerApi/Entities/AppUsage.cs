using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HabitTrackerApi.Entities;

public class AppUsage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [Required]
    public DateTime Date { get; set; } // Using DateTime for EF Core compatibility, but we will store dates with 00:00:00 time.

    [Required]
    public int SecondsSpent { get; set; }
}
