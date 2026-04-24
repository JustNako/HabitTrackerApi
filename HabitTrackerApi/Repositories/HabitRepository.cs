using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApi.Data;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.Repositories;

public class HabitRepository : IHabitRepository
{
    private readonly AppDbContext _context;

    public HabitRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<Habit>> GetUserHabitsAsync(string userId)
    {
        return await _context.Habits
        .Include(h => h.Logs)
        .Where(h => h.UserId == userId)
        .OrderByDescending(h => h.CreatedAt)
        .ToListAsync();
    }

    public async Task<Habit?> GetById(Guid id)
    {
        return await _context.Habits
        .Include(h => h.Logs)
        .FirstOrDefaultAsync(h => h.Id == id);
    }

    public async Task Add(Habit habit)
    {
        await _context.Habits.AddAsync(habit);
    }

    public async Task Save()
    {
        await _context.SaveChangesAsync();
    }
}