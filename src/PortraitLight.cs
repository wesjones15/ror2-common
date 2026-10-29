using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    /// The studio a generated portrait is rendered in: a key from the camera's upper left, a cool
    /// fill opposite it, and a fixed ambient, with the scene's own directional lights, sky ambient
    /// and fog held out of the capture.
    ///
    /// A portrait camera sees only its own layer, but that does not isolate it: scene directional
    /// lights ignore the camera's mask and light the portrait objects anyway, and so does the sky
    /// ambient. A card baked while the game loads its main menu therefore inherited a near-black
    /// ambient and came out as a silhouette. The cards' own light pointed the other way as well: it
    /// travelled *toward* the camera (`LookRotation(-(center - cameraAt))` faces back at the viewer),
    /// so it lit the back of the subject and left the side in the picture unlit. Portrait.cs's
    /// survivor portraits key from the camera's side; this is that side, made explicit and
    /// independent of whichever scene happens to be loaded when the card is baked.
    public sealed class PortraitLight : System.IDisposable
    {
        /// The key's strength for a model drawn at the source's own material colour. The ambient
        /// underneath is a fifth of it and the fill a third, which keeps a lit side near the
        /// material's colour and a shadow side shaped rather than black.
        public const float DefaultKeyIntensity = 1.1f;

        static readonly Color KeyColor = new Color(1f, 0.97f, 0.92f);
        static readonly Color FillColor = new Color(0.72f, 0.80f, 1f);
        static readonly Color StudioAmbient = new Color(0.20f, 0.22f, 0.26f, 0f);
        const float FillRatio = 0.3f;

        readonly GameObject keyGo, fillGo;
        readonly Light key, fill;
        readonly float keyIntensity;
        readonly Color ambient;
        readonly List<Light> sceneLights = new List<Light>();
        readonly AmbientMode ambientMode;
        readonly Color ambientLight;
        readonly float ambientIntensity;
        readonly Light sun;
        readonly bool fog;
        readonly Color fogColor;
        readonly float fogDensity, fogStart, fogEnd;

        /// Takes over the lighting for `layer` until disposed (the caller disposes in its `finally`,
        /// beside the camera and render texture). `exposure` scales the whole studio: a host that
        /// draws its models brighter than the source's material colour passes the reciprocal, so the
        /// card keeps the source's tone instead of washing out.
        public PortraitLight(int layer, float exposure = 1f)
        {
            keyIntensity = DefaultKeyIntensity * exposure;
            ambient = StudioAmbient * exposure;
            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientIntensity = RenderSettings.ambientIntensity;
            sun = RenderSettings.sun;
            fog = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            fogStart = RenderSettings.fogStartDistance;
            fogEnd = RenderSettings.fogEndDistance;
            // Flat ambient with a fixed colour: the sky probe changes as the game loads its menu, and
            // a card must not depend on which moment it was baked.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
                if (light && light.enabled && light.type == LightType.Directional && (light.cullingMask & (1 << layer)) != 0)
                {
                    sceneLights.Add(light);
                    light.enabled = false;
                }
            key = Lamp("portrait key light", layer, KeyColor, keyIntensity);
            keyGo = key.gameObject;
            fill = Lamp("portrait fill light", layer, FillColor, keyIntensity * FillRatio);
            fillGo = fill.gameObject;
            // Models whose see-through batches carry vertex lighting (ModelView.Light) take their light
            // from RenderSettings.sun; with the scene's sun held out of the capture, that is the key.
            if (sceneLights.Count > 0) RenderSettings.sun = key;
        }

        static Light Lamp(string name, int layer, Color color, float intensity)
        {
            var go = new GameObject(name);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            light.cullingMask = 1 << layer;
            return light;
        }

        /// Aims both lamps at `center`, seen from `cameraAt`: the same pose always gives the same
        /// lighting, so a card reframed after its models are drawn (AutoCenter) stays consistent.
        /// A light travels along its own +Z, so both directions below point from the lamp to the subject.
        public void Aim(Vector3 center, Vector3 cameraAt)
        {
            var toSubject = center - cameraAt;
            if (toSubject.sqrMagnitude < 1e-8f) toSubject = Vector3.back;
            toSubject.Normalize();
            var up = Vector3.up - toSubject * Vector3.Dot(Vector3.up, toSubject);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.forward - toSubject * Vector3.Dot(Vector3.forward, toSubject);
            up.Normalize();
            var right = Vector3.Cross(up, toSubject).normalized;
            // Key above and to the left of the camera, so it travels down and to the right.
            keyGo.transform.rotation = Quaternion.LookRotation((toSubject + right * 0.55f - up * 0.5f).normalized, Vector3.up);
            // Fill below and to the right, dim and cool, so the shadow side keeps its form.
            fillGo.transform.rotation = Quaternion.LookRotation((toSubject - right * 0.5f + up * 0.2f).normalized, Vector3.up);
            key.intensity = keyIntensity;
            fill.intensity = keyIntensity * FillRatio;
        }

        public void Dispose()
        {
            if (keyGo) UnityEngine.Object.Destroy(keyGo);
            if (fillGo) UnityEngine.Object.Destroy(fillGo);
            foreach (var light in sceneLights) if (light) light.enabled = true;
            sceneLights.Clear();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }
    }
}
