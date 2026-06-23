using Microsoft.EntityFrameworkCore;
using CommunicationModule.Core.Models;

namespace CommunicationModule.Infrastructure.Data;

public class CommunicationDbContext : DbContext
{
    public CommunicationDbContext(DbContextOptions<CommunicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organisation> Organisations { get; set; } = null!;
    public DbSet<ProviderSubscription> ProviderSubscriptions { get; set; } = null!;
    public DbSet<OpenMRSInstance> OpenMRSInstances { get; set; } = null!;
    public DbSet<Appointment> Appointments { get; set; } = null!;
    public DbSet<NotificationJob> NotificationJobs { get; set; } = null!;
    public DbSet<MessageLog> MessageLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organisation>(eb =>
        {
            eb.ToTable("organisations");
            eb.HasKey(o => o.Id);
            eb.Property(o => o.Name).IsRequired();
        });

        modelBuilder.Entity<ProviderSubscription>(eb =>
        {
            eb.ToTable("providersubscriptions");
            eb.HasKey(p => p.Id);
            eb.Property(p => p.ProviderName).IsRequired();

            eb.HasOne(p => p.Organisation)
                .WithMany(o => o.ProviderSubscriptions)
                .HasForeignKey(p => p.OrganisationId)
                .HasConstraintName("fk_providersubscriptions_organisations_organisationid")
                .OnDelete(DeleteBehavior.Cascade);
        });

            modelBuilder.Entity<OpenMRSInstance>(eb =>
            {
                eb.ToTable("openmrsinstances");
                eb.HasKey(i => i.Id);
                eb.Property(i => i.DisplayName).IsRequired();
                eb.Property(i => i.BaseUrl).IsRequired();
                eb.Property(i => i.ApiVersion).IsRequired();
                eb.Property(i => i.AccessKeyHash).IsRequired();

                eb.HasIndex(i => new { i.OrganisationId, i.BaseUrl }).IsUnique();

                eb.HasOne(i => i.Organisation)
                .WithMany(o => o.OpenMRSInstances)
                .HasForeignKey(i => i.OrganisationId)
                .HasConstraintName("fk_openmrsinstances_organisations_organisationid")
                .OnDelete(DeleteBehavior.Cascade);
            });

        modelBuilder.Entity<Appointment>(eb =>
        {
            eb.ToTable("appointments");
            eb.HasKey(a => a.Id);
            eb.Property(a => a.FhirAppointmentId).IsRequired();

            eb.HasOne(a => a.Organisation)
                .WithMany()
                .HasForeignKey(a => a.OrganisationId)
                .HasConstraintName("fk_appointments_organisations_organisationid")
                .OnDelete(DeleteBehavior.Cascade);

            eb.HasMany(a => a.NotificationJobs)
                .WithOne(nj => nj.Appointment)
                .HasForeignKey(nj => nj.AppointmentId)
                .HasConstraintName("fk_notificationjobs_appointments_appointmentid")
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationJob>(eb =>
        {
            eb.ToTable("notificationjobs");
            eb.HasKey(n => n.Id);
            eb.Property(n => n.ScheduledFor).IsRequired();
        });

        modelBuilder.Entity<MessageLog>(eb =>
        {
            eb.ToTable("messagelogs");
            eb.HasKey(m => m.Id);
            eb.Property(m => m.LoggedAt).IsRequired();
        });
    }
}
