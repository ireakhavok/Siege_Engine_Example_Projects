// Folder: (project)/Scripts
// File: TestSlideController.cs
using SiegeEngine.Core.Definitions;
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Managers;
using SiegeEngine.PlayerSystem;
using SiegeEngine.Systems;
using System;
using System.Numerics;

namespace ProjectScripts
{
    [CustomPlayerController]
    public class TestSlideController : PlayerMovement
    {
        // Distinctly different feel so you can tell it is the custom one immediately
        private const float SlideMaxSpeed = 18.0f;
        private const float SlideAcceleration = 12.0f;
        private const float SlideDeceleration = 8.0f;   // very low friction = ice-slide

        public TestSlideController(InputHandler inputHandler, ClientPredictionSystem predictionSystem, EventBus eventBus = null)
            : base(inputHandler, predictionSystem, eventBus)
        {
            Console.WriteLine("[TestSlideController] Custom ice-slide controller constructed with live services – override active");
        }

        public override void Update(Player player, float deltaTime, Action<int, Vector3, Quaternion> sendMovementRequest, CameraController camera)
        {
            if (player == null || camera == null) return;

            // Re-use the base input collection (ActiveKeys / MovementInput) that the base class already maintains
            // but apply completely different physics numbers so the difference is obvious.

            float yawRad = camera.Yaw * (float)(Math.PI / 180);
            Vector3 forward = Vector3.Normalize(new Vector3((float)Math.Sin(yawRad), (float)Math.Cos(yawRad), 0));
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));

            // We still need the movement input that the base class is tracking.
            // Because the fields are private we simply call the base method first
            // (it updates velocity/position with the stock numbers) then immediately
            // overwrite the result with our slide numbers. This keeps the test simple
            // and still proves the custom type is the one that is running.
            base.Update(player, deltaTime, sendMovementRequest, camera);

            // Now replace the velocity that base just wrote with a high-momentum version
            Vector3 vel = player.Physics.Velocity;
            float speed = vel.Length();
            if (speed > 0.01f)
            {
                // stretch the velocity toward the higher top speed and lower friction
                float targetSpeed = Math.Min(speed * 1.35f, SlideMaxSpeed);
                vel = Vector3.Normalize(vel) * targetSpeed;
                player.Physics.Velocity = vel;
            }

            // Keep the position in sync with the new velocity for this frame
            Vector3 pos = player.Physics.Position + vel * deltaTime;
            pos.X = Math.Clamp(pos.X, 0, 12500f);
            pos.Y = Math.Clamp(pos.Y, 0, 7500f);
            player.Physics.Position = pos;
        }
    }
}