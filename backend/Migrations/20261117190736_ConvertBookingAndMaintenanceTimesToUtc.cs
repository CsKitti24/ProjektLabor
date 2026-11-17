using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <summary>
    /// A foglalások és karbantartási idősávok kezdő/záró időpontját korábban a frontendtől kapott
    /// budapesti helyi időben (megjelölés nélkül) tároltuk, miközben minden más időpont UTC volt.
    /// Ez a migráció a meglévő sorokat UTC-re alakítja, hogy minden időpont egységesen UTC legyen.
    /// A 'Central European Standard Time' a Windows-azonosítója az Europe/Budapest időzónának.
    /// </summary>
    public partial class ConvertBookingAndMaintenanceTimesToUtc : Migration
    {
        private const string LocalZone = "Central European Standard Time";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Foglalasok", "KarbantartasiIdosavok" })
            {
                migrationBuilder.Sql($"""
                    UPDATE [{table}]
                    SET [StartTime] = CONVERT(datetime2, [StartTime] AT TIME ZONE '{LocalZone}' AT TIME ZONE 'UTC'),
                        [EndTime] = CONVERT(datetime2, [EndTime] AT TIME ZONE '{LocalZone}' AT TIME ZONE 'UTC');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Foglalasok", "KarbantartasiIdosavok" })
            {
                migrationBuilder.Sql($"""
                    UPDATE [{table}]
                    SET [StartTime] = CONVERT(datetime2, [StartTime] AT TIME ZONE 'UTC' AT TIME ZONE '{LocalZone}'),
                        [EndTime] = CONVERT(datetime2, [EndTime] AT TIME ZONE 'UTC' AT TIME ZONE '{LocalZone}');
                    """);
            }
        }
    }
}
