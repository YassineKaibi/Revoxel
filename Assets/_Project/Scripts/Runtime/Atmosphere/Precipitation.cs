using UnityEngine;

namespace Revoxel.Atmosphere
{
    // One particle system of rain or snow in a box around the camera (D-100), built in code.
    // Particles live in world space; the box follows the camera.
    public sealed class Precipitation
    {
        private readonly ParticleSystem _system;
        private readonly Transform _transform;
        private readonly float _maxRate;
        private readonly float _fallSpeed;
        private readonly float _height;
        private float _rate = -1f;
        private float _wind = float.NaN;

        public Precipitation(
            Transform parent,
            string name,
            Material material,
            bool snow,
            float radius,
            float maxRate
        )
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _transform = go.transform;
            _system = go.AddComponent<ParticleSystem>();
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _maxRate = maxRate;
            _fallSpeed = snow ? 1.3f : 9f;
            _height = snow ? 8f : 12f;

            var main = _system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = (_height + 4f) / _fallSpeed;
            main.startSpeed = 0f;
            main.startSize = snow ? 0.05f : 0.015f;
            main.maxParticles = (int)(maxRate * ((_height + 4f) / _fallSpeed) * 1.1f);
            main.gravityModifier = 0f;

            var shape = _system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2f * radius, 0.5f, 2f * radius);
            shape.position = new Vector3(0f, _height, 0f);

            var emission = _system.emission;
            emission.rateOverTime = 0f;

            var noise = _system.noise;
            noise.enabled = snow;
            noise.strength = 0.6f;
            noise.frequency = 0.4f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = snow
                ? ParticleSystemRenderMode.Billboard
                : ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = snow ? 0f : 0.045f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetWind(0f);
            _system.Play();
        }

        // Follows the camera; amount 0 to 1 of the maximum rate.
        public void Update(Vector3 cameraPosition, float amount, float windMetersPerSecond)
        {
            _transform.position = cameraPosition;
            var rate = Mathf.Round(Mathf.Clamp01(amount) * _maxRate / 50f) * 50f;
            if (!Mathf.Approximately(rate, _rate))
            {
                _rate = rate;
                var emission = _system.emission;
                emission.rateOverTime = rate;
            }

            SetWind(Mathf.Round(windMetersPerSecond * 2f) / 2f);
        }

        public void Destroy()
        {
            if (_transform != null)
            {
                Object.Destroy(_transform.gameObject);
            }
        }

        private void SetWind(float wind)
        {
            if (wind == _wind)
            {
                return;
            }

            _wind = wind;
            var velocity = _system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind * 0.6f);
            velocity.y = new ParticleSystem.MinMaxCurve(-_fallSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(wind * 0.3f);
        }
    }
}
