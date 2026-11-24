using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodHub.Core.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve every original SQL check, computed column, procedure and table type.
            // Each GO-delimited batch is a separate command; GO itself is not SQL syntax.
            foreach (var batch in BaselineSql.Batches()) migrationBuilder.Sql(batch);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An accidental rollback must never destroy existing customer/order data.
            throw new NotSupportedException("The historical baseline cannot be rolled back. Restore a reviewed backup instead.");
        }
    }
}
