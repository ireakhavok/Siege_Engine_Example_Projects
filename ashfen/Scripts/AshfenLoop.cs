// Folder: ashfen/Scripts
// File: AshfenLoop.cs
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.PlayerSystem;
using SiegeEngine.Systems;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace ProjectScripts
{
    [RegisterGameSystem]
    public sealed class AshfenLoop : GameSystem
    {
        private const float PlayerMaxHp = 100f;
        private const float HostileHp = 40f;
        private const float BaseAttack = 12f;
        private const float ArmedAttack = 28f;
        private const float AttackRange = 2.4f;
        private const float AttackCooldown = 0.45f;
        private const float AggroRange = 9f;
        private const float ContactRange = 1.5f;
        private const float ContactDamage = 8f;
        private const float ContactCooldown = 0.85f;
        private const float ChaseSpeed = 3.2f;
        private const float PickupRange = 2.2f;
        private const float PotionHeal = 45f;
        private const string BladeId = "ashfen_blade";
        private const string DraughtId = "ashfen_draught";

        private readonly EventBus _eventBus;
        private readonly Dictionary<int, float> _contactReadyAt = new Dictionary<int, float>();
        private readonly HashSet<int> _picked = new HashSet<int>();

        private bool _playerReady;
        private bool _dead;
        private bool _combatOpen;
        private bool _bagOpen;
        private bool _bagShown;
        private float _attackReadyAt;
        private float _attackDamage = BaseAttack;
        private float _clock;
        private string _combatMarkup = "";
        private string _bagMarkup = "";

        public AshfenLoop(IGameServer server, EventBus eventBus)
            : this(server, eventBus, null, null)
        {
        }

        public AshfenLoop(IGameServer server, EventBus eventBus, InputHandler inputHandler)
            : this(server, eventBus, inputHandler, null)
        {
        }

        public AshfenLoop(IGameServer server, EventBus eventBus, InputHandler inputHandler, ModelManager modelManager)
            : base(server)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _eventBus.Subscribe<KeyInputEvent>(OnNetworkKey);
            _eventBus.Subscribe<ItemPickedUpEvent>(OnItemPickedUp);
            if (inputHandler != null)
                inputHandler.KeyEvent += OnKey;
        }

        public override void Update(float deltaTime)
        {
            if (deltaTime < 0f) deltaTime = 0f;
            _clock += deltaTime;

            Player player = FindPlayer();
            if (player == null || player.Physics == null)
                return;

            if (!_playerReady)
            {
                player.Physics.Health = PlayerMaxHp;
                _playerReady = true;
            }

            if (player.Physics.Health <= 0f)
            {
                player.Physics.Health = 0f;
                _dead = true;
            }

            Vector3 playerPos = player.Physics.Position;
            int fallen = 0;
            int hostileCount = 0;
            var taken = new List<Entity>();

            IReadOnlyList<Entity> entities = _server.GetEntities();
            if (entities != null)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    Entity entity = entities[i];
                    if (entity == null || entity.Type == null) continue;
                    if (IsHostile(entity))
                    {
                        hostileCount++;
                        if (TickHostile(entity, player, playerPos, deltaTime))
                            fallen++;
                    }
                    else if (!_dead && PickupItemId(entity) != null)
                    {
                        if (InPickupRange(entity, playerPos))
                            taken.Add(entity);
                    }
                }
            }

            for (int i = 0; i < taken.Count; i++)
                TakePickup(taken[i], player);

            PresentCombat(player.Physics.Health, fallen, hostileCount);
        }

        private bool TickHostile(Entity hostile, Player player, Vector3 playerPos, float deltaTime)
        {
            PhysicsComponent body = hostile.Physics;
            if (body == null) return false;
            if (body.Health <= 0f)
            {
                body.Health = 0f;
                body.Velocity = Vector3.Zero;
                return true;
            }

            if (_dead) return false;

            Vector3 pos = body.Position;
            float dx = playerPos.X - pos.X;
            float dy = playerPos.Y - pos.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < AggroRange && dist > 0.05f)
            {
                float step = ChaseSpeed * deltaTime;
                if (step > dist) step = dist;
                float inv = 1f / dist;
                pos.X += dx * inv * step;
                pos.Y += dy * inv * step;
                body.Position = pos;
                body.Velocity = Vector3.Zero;
                dist -= step;
            }

            if (dist <= ContactRange)
            {
                if (!_contactReadyAt.TryGetValue(hostile.Id, out float ready) || _clock >= ready)
                {
                    _contactReadyAt[hostile.Id] = _clock + ContactCooldown;
                    player.Physics.Health -= ContactDamage;
                    if (player.Physics.Health <= 0f)
                    {
                        player.Physics.Health = 0f;
                        _dead = true;
                    }
                }
            }

            return false;
        }

        private bool InPickupRange(Entity pickup, Vector3 playerPos)
        {
            if (pickup == null || _picked.Contains(pickup.Id) || pickup.Physics == null) return false;
            float dx = playerPos.X - pickup.Physics.Position.X;
            float dy = playerPos.Y - pickup.Physics.Position.Y;
            return dx * dx + dy * dy <= PickupRange * PickupRange;
        }

        private void TakePickup(Entity pickup, Player player)
        {
            if (pickup == null || player == null || _picked.Contains(pickup.Id)) return;
            string itemId = PickupItemId(pickup);
            if (string.IsNullOrEmpty(itemId)) return;
            _picked.Add(pickup.Id);
            _server.RemoveEntity(pickup.Id);
            _eventBus.Publish(new ItemPickedUpEvent(player.EntityId, itemId));
        }

        private void OnItemPickedUp(ItemPickedUpEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.ItemId)) return;
            if (e.ItemId != BladeId && e.ItemId != DraughtId) return;

            Entity entity = _server.GetEntityById(e.EntityId);
            if (entity == null) return;
            InventoryComponent inv = entity.GetComponent<InventoryComponent>();
            if (inv == null)
            {
                inv = new InventoryComponent();
                entity.AddComponent(inv);
            }
            if (inv.Items.ContainsKey(e.ItemId)) return;

            Player player = entity.GetComponent<Player>();
            if (e.ItemId == BladeId)
            {
                _attackDamage = ArmedAttack;
            }
            else if (player != null && player.Physics != null && !_dead)
            {
                float hp = player.Physics.Health + PotionHeal;
                if (hp > PlayerMaxHp) hp = PlayerMaxHp;
                player.Physics.Health = hp;
            }

            string name = e.ItemId == BladeId ? "Courtyard blade" : "Draught";
            float stat = e.ItemId == BladeId ? ArmedAttack : PotionHeal;
            string statKey = e.ItemId == BladeId ? "Damage" : "Heal";
            inv.AddItem(new InventoryComponent.Item
            {
                Id = e.ItemId,
                Name = name,
                Tier = 1,
                Rarity = e.ItemId == BladeId ? InventoryComponent.Rarity.Uncommon : InventoryComponent.Rarity.Common,
                Stats = new Dictionary<string, float> { { statKey, stat } },
                Level = 1,
                StackSize = 1
            });
            _server.ValidateInventory(e.EntityId, "AddItem", e.ItemId);
            if (_bagOpen)
                PresentBag(entity, true);
        }

        private void OnNetworkKey(KeyInputEvent e)
        {
            if (e == null) return;
            OnKey(e.Key, e.Action);
        }

        private void OnKey(Key key, InputAction action)
        {
            if (action != InputAction.Press) return;
            if (key == Key.I)
            {
                _bagOpen = !_bagOpen;
                Player player = FindPlayer();
                Entity entity = null;
                if (player != null)
                    entity = _server.GetEntityById(player.EntityId);
                PresentBag(entity, _bagOpen);
                return;
            }
            if (key != Key.F || _dead) return;
            if (_clock < _attackReadyAt) return;
            _attackReadyAt = _clock + AttackCooldown;
            Strike();
        }

        private void Strike()
        {
            Player player = FindPlayer();
            if (player == null || player.Physics == null) return;
            Vector3 origin = player.Physics.Position;
            Entity best = null;
            float bestDist = AttackRange;
            IReadOnlyList<Entity> entities = _server.GetEntities();
            if (entities == null) return;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!IsHostile(entity) || entity.Physics == null) continue;
                if (entity.Physics.Health <= 0f) continue;
                float dx = origin.X - entity.Physics.Position.X;
                float dy = origin.Y - entity.Physics.Position.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = entity;
                }
            }
            if (best == null) return;
            best.Physics.Health -= _attackDamage;
            if (best.Physics.Health < 0f) best.Physics.Health = 0f;
        }

        // Placed in AshfenCourtyard. Type stays FBX so the mesh loads; these ids are the markers.
        private const int HostileA = 21;
        private const int HostileB = 22;
        private const int HostileC = 23;
        private const int BladeEntityId = 31;
        private const int DraughtEntityId = 32;

        private static bool IsHostile(Entity entity)
        {
            if (entity == null) return false;
            if (entity.Type == "AshfenHostile") return true;
            int id = entity.Id;
            return id == HostileA || id == HostileB || id == HostileC;
        }

        private static string PickupItemId(Entity entity)
        {
            if (entity == null) return null;
            if (entity.Type != null && entity.Type.StartsWith("AshfenPickup:", StringComparison.Ordinal))
            {
                string itemId = entity.Type.Substring("AshfenPickup:".Length);
                return itemId.Length == 0 ? null : itemId;
            }
            if (entity.Id == BladeEntityId) return BladeId;
            if (entity.Id == DraughtEntityId) return DraughtId;
            return null;
        }

        private Player FindPlayer()
        {
            IReadOnlyList<Entity> entities = _server.GetEntities();
            if (entities == null) return null;
            for (int i = 0; i < entities.Count; i++)
            {
                Player player = entities[i]?.GetComponent<Player>();
                if (player != null) return player;
            }
            return null;
        }

        private void PresentCombat(float hp, int fallen, int hostileCount)
        {
            if (hp < 0f) hp = 0f;
            string state = _dead ? "Dead. The ruin keeps you." : (fallen >= 3 && hostileCount > 0 ? "The courtyard is quiet." : "Clear the three.");
            string html = CombatHtml(hp, _attackDamage, fallen, state);
            Present("AshfenHud", html, "Ashfen", HudAnchor.Top, 440f, 168f, true, ref _combatOpen, ref _combatMarkup);
        }

        private void PresentBag(Entity playerEntity, bool open)
        {
            string html = BagHtml(playerEntity);
            Present("AshfenBag", html, "Bag", HudAnchor.Right, 248f, 360f, open, ref _bagShown, ref _bagMarkup);
        }

        private void Present(string key, string html, string title, HudAnchor anchor, float width, float height, bool open, ref bool shown, ref string last)
        {
            if (!open)
            {
                if (!shown) return;
                _eventBus.Publish(new OpenGameHudEvent
                {
                    Key = key,
                    Title = title,
                    Open = false,
                    Anchor = anchor,
                    Chrome = PanelChromeStyle.Bare,
                    Docking = DockingMode.Desktop,
                    AllowMove = true,
                    Width = width,
                    Height = height
                });
                shown = false;
                return;
            }

            if (shown && last == html) return;
            if (shown)
            {
                _eventBus.Publish(new OpenGameHudEvent
                {
                    Key = key,
                    Title = title,
                    Open = false,
                    Anchor = anchor,
                    Chrome = PanelChromeStyle.Bare,
                    Docking = DockingMode.Desktop,
                    AllowMove = true,
                    Width = width,
                    Height = height
                });
            }

            _eventBus.Publish(new OpenGameHudEvent
            {
                Key = key,
                Title = title,
                HtmlContent = html,
                HtmlRelativePath = key == "AshfenBag" ? "AshfenBag.html" : "AshfenHud.html",
                Open = true,
                Anchor = anchor,
                Chrome = PanelChromeStyle.Bare,
                Docking = DockingMode.Desktop,
                AllowMove = true,
                Width = width,
                Height = height
            });
            shown = true;
            last = html;
        }

        private static string CombatHtml(float hp, float attack, int fallen, string state)
        {
            string hpText = hp.ToString("0", CultureInfo.InvariantCulture);
            string atkText = attack.ToString("0", CultureInfo.InvariantCulture);
            string cls = state.StartsWith("Dead", StringComparison.Ordinal) ? "dead" : (fallen >= 3 ? "win" : "");
            return "<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'><style>"
                + "html,body{margin:0;padding:0;width:100%;height:100%;background:transparent;overflow:hidden;}"
                + ".hud{margin:8px;padding:10px 12px;background:rgba(16,14,12,0.88);border:1px solid #6e5642;color:#eadcc8;font-family:Georgia,serif;}"
                + ".hud h1{margin:0 0 6px;font-size:13px;letter-spacing:3px;color:#d4b483;}"
                + ".row{font-size:13px;margin:2px 0;}"
                + ".dead{color:#c45a4a;}.win{color:#d4b483;}"
                + "</style></head><body><div class='hud'><h1>ASHFEN</h1>"
                + "<div class='row'>HP " + hpText + " / 100</div>"
                + "<div class='row'>Attack " + atkText + "</div>"
                + "<div class='row'>Courtyard " + fallen.ToString(CultureInfo.InvariantCulture) + " / 3</div>"
                + "<div class='row " + cls + "'>" + state + "</div>"
                + "</div></body></html>";
        }

        private static string BagHtml(Entity playerEntity)
        {
            var rows = new System.Text.StringBuilder();
            InventoryComponent inv = playerEntity?.GetComponent<InventoryComponent>();
            if (inv == null || inv.Items.Count == 0)
            {
                rows.Append("<div class='empty'>Empty. Walk into the blade or the draught.</div>");
            }
            else
            {
                foreach (KeyValuePair<string, InventoryComponent.Item> pair in inv.Items)
                {
                    InventoryComponent.Item item = pair.Value;
                    string name = item != null && !string.IsNullOrEmpty(item.Name) ? item.Name : pair.Key;
                    rows.Append("<div class='item'>");
                    rows.Append(System.Net.WebUtility.HtmlEncode(name));
                    rows.Append("</div>");
                }
            }

            return "<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'><style>"
                + "html,body{margin:0;padding:0;width:100%;height:100%;background:#14110e;overflow:hidden;}"
                + ".inv{padding:12px;color:#eadcc8;font-family:Georgia,serif;}"
                + ".inv h1{margin:0 0 8px;font-size:14px;letter-spacing:3px;text-align:center;color:#d4b483;}"
                + ".item{border:1px solid #5a4636;background:#26201a;padding:8px;margin:0 0 6px;font-size:13px;}"
                + ".empty{font-size:12px;color:#8a7b6a;}"
                + "</style></head><body><div class='inv'><h1>BAG</h1>"
                + rows
                + "</div></body></html>";
        }
    }
}
