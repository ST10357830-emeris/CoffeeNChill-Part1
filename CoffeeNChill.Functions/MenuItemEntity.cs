using System;
using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Functions
{
    // Implements ITableEntity to enable direct binding with Azure Table Storage APIs
    public class MenuItemEntity : ITableEntity
    {
        // Category string (e.g., "Hot Drinks", "Pastries") acting as the Azure Table PartitionKey
        public string PartitionKey { get; set; } = string.Empty;

        // Unique SKU or ID (e.g., "COF-001") acting as the Azure Table RowKey within a partition
        public string RowKey { get; set; } = string.Empty;

        // Name of the menu item (e.g., "Espresso", "Ham & Cheese Croissant")
        public string Name { get; set; } = string.Empty;

        // Short descriptive text explaining the menu item
        public string Description { get; set; } = string.Empty;

        // Price of the menu item in Double precision format
        public double Price { get; set; }

        // Flag indicating whether the canteen currently has this item in stock/available
        public bool IsAvailable { get; set; }

        // Optimistic concurrency token initialized to ETag.All wildcard (*) to allow updating records
        public ETag ETag { get; set; } = ETag.All;

        // Automatic timestamp populated by Azure Table Storage on record persist
        public DateTimeOffset? Timestamp { get; set; }
    }
}