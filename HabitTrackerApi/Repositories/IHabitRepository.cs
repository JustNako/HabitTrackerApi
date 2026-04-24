using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.Repositories;

public interface IHabitRepository
{
    Task<List<Habit>> GetUserHabitsAsync(string userId);
    Task<Habit?> GetById(Guid id);
    Task Add(Habit habit);
    Task Save();
}