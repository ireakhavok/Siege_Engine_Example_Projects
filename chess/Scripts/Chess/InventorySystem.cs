// Folder: chess/Scripts/Chess
// File: InventorySystem.cs
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Systems;
using System;

namespace ChessProject
{
    [RegisterGameSystem]
    public sealed class InventorySystem : GameSystem
    {
        private readonly EventBus _eventBus;
        private bool _seeded;

        public InventorySystem(IGameServer server, EventBus eventBus) : base(server)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _eventBus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
            Console.WriteLine("[InventorySystem] registered");
        }

        public override void Update(float deltaTime)
        {
            if (_seeded) return;
            var entities = _server.GetEntities();
            if (entities == null || entities.Count == 0) return;
            _seeded = true;
            int id = entities[0].Id;
            Give(id, "white_pawn");
            Give(id, "white_knight");
        }

        private void OnItemPickedUp(ItemPickedUpEvent e)
        {
            if (e == null) return;
            Give(e.EntityId, e.ItemId);
        }

        private void Give(int entityId, string itemId)
        {
            Entity entity = _server.GetEntityById(entityId);
            if (entity == null) return;
            var inv = entity.GetComponent<InventoryComponent>();
            if (inv == null)
            {
                inv = new InventoryComponent();
                entity.AddComponent(inv);
            }
            bool added = inv.AddItem(new InventoryComponent.Item
            {
                Id = itemId,
                Name = itemId,
                Tier = 1,
                Rarity = InventoryComponent.Rarity.Common,
                Level = 1,
                StackSize = 1
            });
            Console.WriteLine("[InventorySystem] entity=" + entityId + " item=" + itemId + " ok=" + added + " slots=" + inv.Items.Count);
            _server.ValidateInventory(entityId, "AddItem", itemId);
        }
    }
}
