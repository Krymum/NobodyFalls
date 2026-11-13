using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NobodyFalls.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The base wardrobe and the first unlockable items, with colours from the reference
    /// scene. Hard-coded on purpose: a migration must never change once released. Later
    /// catalogue edits happen in the database (admin, phase 2).
    /// </summary>
    public partial class SeedWardrobe : Migration
    {
        private static readonly string[] Columns =
            ["id", "slot", "rarity", "render_key", "colors", "unlock_metric", "unlock_threshold", "active", "sort"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            string[] coats = ["#4A5463", "#6A6744", "#2E4749", "#4E5A3C", "#5A2E39", "#857A65"];
            string[] hair = ["#2A1E19", "#CFC7BA", "#4A3A2C", "#1A1A1C"];

            migrationBuilder.InsertData(
                table: "skin_items",
                columns: Columns,
                values: new object[,]
                {
                    { "coat-long", "coat", "base", "coat.long", coats, null, null, true, 0 },
                    { "coat-hooded", "coat", "base", "coat.hooded", coats, null, null, true, 1 },
                    { "trousers-plain", "trousers", "base", "trousers.plain", new[] { "#262A31", "#33373E" }, null, null, true, 2 },
                    { "boots-plain", "boots", "base", "boots.plain", new[] { "#16191E", "#2A2622" }, null, null, true, 3 },
                    { "hair-short", "hair", "base", "hair.short", hair, null, null, true, 4 },
                    { "hair-long", "hair", "base", "hair.long", hair, null, null, true, 5 },
                    { "headwear-beanie", "headwear", "base", "headwear.beanie", new[] { "#7A3931", "#585C63", "#3F4A52" }, null, null, true, 6 },
                    { "headwear-hood", "headwear", "base", "headwear.hood", coats, null, null, true, 7 },
                    { "headwear-brim", "headwear", "base", "headwear.brim", new[] { "#34343A", "#4B4237" }, null, null, true, 8 },
                    { "scarf-wool", "scarf", "base", "scarf.wool", new[] { "#B5AD9C", "#6F757E", "#5E4B3C" }, null, null, true, 9 },
                    { "gloves-wool", "gloves", "base", "gloves.wool", new[] { "#3B3F45", "#2E3238" }, null, null, true, 10 },
                    { "scarf-rescuer", "scarf", "unlockable", "scarf.long", new[] { "#8C8A7E", "#4F5B66" }, "rescues", 3, true, 11 },
                    { "gloves-strangers", "gloves", "unlockable", "gloves.fingerless", new[] { "#5B5048", "#3D4650" }, "strangers_held", 10, true, 12 },
                    { "coat-storm", "coat", "unlockable", "coat.oilskin", new[] { "#3A4A4F", "#4D4A3A" }, "storms_survived", 1, true, 13 },
                    { "headwear-guardian", "headwear", "unlockable", "headwear.wide-brim", new[] { "#2F3236", "#4A4038" }, "guardian_score", 100, true, 14 },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("delete from skin_items");
        }
    }
}
