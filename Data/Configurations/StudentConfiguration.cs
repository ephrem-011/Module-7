using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TmsApi.Entities;

namespace TmsApi.Data.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.RegistrationNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(s => s.age).HasMaxLength(3);

        builder.HasIndex(s => s.RegistrationNumber)
            .IsUnique();

        builder.Property<DateTime>("LastUpdated");

        builder.Property(s => s.Versionn)
            .IsRowVersion();
    }
}