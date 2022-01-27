using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;

namespace BlendoBot.Module.RemindMe;

internal class RemindMeDbContext : DbContext {
	private RemindMeDbContext(DbContextOptions<RemindMeDbContext> options) : base(options) { }
	public DbSet<Reminder> Reminders { get; set; }
	private DbSet<Settings> SettingsSet { get; set; }
	public Settings Settings {
		get {
			if (!SettingsSet.Any()) {
				SettingsSet.Add(new Settings() {
					MinimumRepeatTime = 300ul,
					MaximumRemindersPerPerson = 20
				});
				SaveChanges();
			}
			return SettingsSet.Single();
		}
	}

	public static RemindMeDbContext Get(RemindMe module) {
		DbContextOptionsBuilder<RemindMeDbContext> optionsBuilder = new();
		optionsBuilder.UseSqlite($"Data Source={Path.Combine(module.FilePathProvider.GetDataDirectoryPath(module), "blendobot-remindme-database.db")}");
		RemindMeDbContext dbContext = new(optionsBuilder.Options);
		dbContext.Database.EnsureCreated();
		return dbContext;
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		base.OnModelCreating(modelBuilder);
		modelBuilder.Entity<Reminder>().Property(r => r.Time).HasConversion(t => t, t => DateTime.SpecifyKind(t, DateTimeKind.Utc));
	}
}
