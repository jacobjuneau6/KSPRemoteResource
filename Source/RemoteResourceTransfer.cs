using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace RemoteResourceTransfer
{
    // ============================================================================
    // Data class: aggregated resource info for a vessel
    // ============================================================================
    public class VesselResourceSummary
    {
        public string Name;          // e.g. "LiquidFuel"
        public int Id;               // PartResourceLibrary definition id
        public double Amount;        // total units across all parts on the vessel
        public double Capacity;      // total maxAmount across all parts
        public double Available => Amount;
        public double Spare => Math.Max(0.0, Capacity - Amount);
        public bool FlowState;       // PartResource.flowState (true = can flow)
    }

    // ============================================================================
    // Main KSPAddon — one per Flight scene
    // ============================================================================
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RemoteResourceTransfer : MonoBehaviour
    {
        public static RemoteResourceTransfer Instance { get; private set; }

        // ---- settings (modifiable in-flight or via config later) ----
        public float maxTransferRange = 1000f;        // metres
        public double electricCostPerUnit = 0.000;    // EC drained from active vessel per unit transferred
        public double electricCostBase       = 0.0;   // flat EC cost per transfer

        // ---- toolbar ----
        private ApplicationLauncherButton _launcherButton;

        // ---- GUI state ----
        private bool   _visible;
        private Rect   _windowRect   = new Rect(280, 80, 560, 520);
        private int    _windowId     = 87654321;
        private Vector2 _scrollResources;

        // ---- transfer selection ----
        private int     _sourceVesselIdx = -1;
        private int     _destVesselIdx   = -1;
        private string  _statusMessage   = "";
        private float   _statusFadeTimer;

        // Per-resource slider values keyed by resource id
        private Dictionary<int, float> _transferSliders = new Dictionary<int, float>();

        // ---- cached per-frame data (rebuilt in OnGUI for simplicity) ----
        private List<Vessel>                _vesselsInRange  = new List<Vessel>();
        private List<VesselResourceSummary> _sourceResources = new List<VesselResourceSummary>();
        private List<VesselResourceSummary> _destResources   = new List<VesselResourceSummary>();
        private double                      _availableEC;

        // ====================================================================
        // Unity / KSP lifecycle
        // ====================================================================

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
            if (ApplicationLauncher.Ready)
                OnLauncherReady();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
            RemoveLauncher();
        }

        // ====================================================================
        // Toolbar button
        // ====================================================================

        private void OnLauncherReady()
        {
            if (_launcherButton != null) return;
            // Generate a simple directional-arrow icon at runtime
            Texture2D icon = GenerateIcon();
            _launcherButton = ApplicationLauncher.Instance.AddModApplication(
                () => _visible = true,      // onTrue  (toggle on)
                () => _visible = false,     // onFalse (toggle off)
                null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT,
                icon);
        }

        private void OnLauncherDestroyed()
        {
            RemoveLauncher();
        }

        private void RemoveLauncher()
        {
            if (_launcherButton == null) return;
            ApplicationLauncher.Instance.RemoveModApplication(_launcherButton);
            _launcherButton = null;
        }

        /// <summary>
        /// Generate a simple 38×38 icon: green "transfer" arrows.
        /// Falls back to a solid-colour square if texture ops fail.
        /// </summary>
        private Texture2D GenerateIcon()
        {
            const int s = 38;
            var tex = new Texture2D(s, s, TextureFormat.ARGB32, false);
            Color bg   = new Color(0.15f, 0.15f, 0.15f, 1f);
            Color fg   = new Color(0.2f,  0.9f,  0.3f,  1f);  // green
            Color white = Color.white;

            // Fill background
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, bg);

            // Draw a right-pointing arrow in the top half and left-pointing in bottom half
            DrawArrow(tex,  4, 10, 14, 12, fg,    right: true);
            DrawArrow(tex, 20, 10, 14, 12, white,  right: false);
            tex.Apply();
            return tex;
        }

        private void DrawArrow(Texture2D tex, int ox, int oy, int w, int h, Color c, bool right)
        {
            int my = oy + h / 2;
            for (int y = oy; y < oy + h; y++)
            {
                int dy = Math.Abs(y - my);
                int halfH = (h - dy * 2) / 2;
                if (halfH <= 0) halfH = 1;
                int xStart = right ? ox : ox + w - halfH;
                int xEnd   = right ? ox + halfH : ox + w;
                for (int x = xStart; x <= xEnd && x < 38 && x >= 0; x++)
                {
                    if (x >= 0 && x < 38 && y >= 0 && y < 38)
                        tex.SetPixel(x, y, c);
                }
            }
            // Arrowhead
            int ax = right ? ox + w / 2 + 2 : ox + w / 2 - 2;
            for (int dy = -3; dy <= 3; dy++)
            {
                int px = ax + (right ? Math.Abs(dy) : -Math.Abs(dy));
                int py = my + dy;
                if (px >= 0 && px < 38 && py >= 0 && py < 38)
                    tex.SetPixel(px, py, c);
            }
        }

        // ====================================================================
        // IMGUI window
        // ====================================================================

        private void OnGUI()
        {
            if (!_visible) return;

            // Lock / unlock click-through when mouse is over our window
            _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindow,
                                           "Remote Resource Transfer",
                                           GUILayout.MinWidth(400), GUILayout.MinHeight(300));
        }

        private void DrawWindow(int id)
        {
            RebuildCache();

            GUILayout.BeginVertical();
            {
                DrawRangeInfo();
                GUILayout.Space(4);
                DrawVesselSelectors();
                GUILayout.Space(8);
                DrawResourceSliders();
                GUILayout.Space(8);
                DrawActionButtons();
                GUILayout.Space(4);
                DrawStatus();
            }
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }

        // ====================================================================
        // GUI sub-sections
        // ====================================================================

        private void DrawRangeInfo()
        {
            GUILayout.Label($"Transfer range: <b>{maxTransferRange:F0} m</b>  |  Vessels in range: <b>{_vesselsInRange.Count - 1}</b> (excluding self)");
            GUILayout.Label($"Available EC on active vessel: <b>{_availableEC:F1}</b>");
        }

        private void DrawVesselSelectors()
        {
            string[] names = _vesselsInRange.Select(v => $"{v.vesselName}  [{Vector3.Distance(v.transform.position, FlightGlobals.ActiveVessel.transform.position):F0} m]").ToArray();
            if (names.Length == 0)
            {
                GUILayout.Label("<color=yellow>No vessels in range.</color>");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Source:", GUILayout.Width(55));
            int newSrc = GUILayout.SelectionGrid(_sourceVesselIdx, names, 1, GUILayout.Width(240));
            if (newSrc != _sourceVesselIdx)
            {
                _sourceVesselIdx = newSrc;
                _transferSliders.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dest:", GUILayout.Width(55));
            int newDst = GUILayout.SelectionGrid(_destVesselIdx, names, 1, GUILayout.Width(240));
            if (newDst != _destVesselIdx)
            {
                _destVesselIdx = newDst;
                _transferSliders.Clear();
            }
            GUILayout.EndHorizontal();

            // Prevent source == dest
            if (_sourceVesselIdx >= 0 && _sourceVesselIdx == _destVesselIdx)
            {
                GUILayout.Label("<color=orange>Source and destination are the same vessel.</color>");
            }
        }

        private void DrawResourceSliders()
        {
            if (_sourceVesselIdx < 0 || _destVesselIdx < 0) return;
            if (_sourceVesselIdx == _destVesselIdx) return;
            if (_sourceResources.Count == 0)
            {
                GUILayout.Label("<color=grey>Source vessel has no transferable resources.</color>");
                return;
            }

            GUILayout.Label("<b>Resources to transfer:</b>");
            _scrollResources = GUILayout.BeginScrollView(_scrollResources, GUILayout.Height(200));
            {
                for (int i = 0; i < _sourceResources.Count; i++)
                {
                    VesselResourceSummary src = _sourceResources[i];
                    VesselResourceSummary dst = _destResources.FirstOrDefault(r => r.Id == src.Id);

                    double maxFromSrc = src.Available;
                    double maxToDst   = dst?.Spare ?? 0.0;
                    double maxMove    = Math.Min(maxFromSrc, maxToDst);

                    if (!_transferSliders.TryGetValue(src.Id, out float val))
                        val = 0f;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{src.Name}", GUILayout.Width(120));
                    GUILayout.Label($"Avail: {src.Available:F1}", GUILayout.Width(80));
                    GUILayout.Label($"Space: {maxToDst:F1}", GUILayout.Width(80));

                    float newVal = GUILayout.HorizontalSlider(val, 0f, (float)maxMove, GUILayout.Width(140));
                    string amtText = GUILayout.TextField(newVal.ToString("F1"), GUILayout.Width(60));
                    if (float.TryParse(amtText, out float parsed))
                        newVal = Mathf.Clamp(parsed, 0f, (float)maxMove);

                    _transferSliders[src.Id] = newVal;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawActionButtons()
        {
            if (_sourceVesselIdx < 0 || _destVesselIdx < 0) return;
            if (_sourceVesselIdx == _destVesselIdx) return;

            float total = _transferSliders.Values.Sum();
            bool hasTransfer = total > 0.001f;

            GUILayout.BeginHorizontal();
            {
                GUI.enabled = hasTransfer;
                if (GUILayout.Button("TRANSFER", GUILayout.Height(30)))
                    ExecuteTransfers();
                GUI.enabled = true;

                if (GUILayout.Button("Reset All", GUILayout.Height(30)))
                    _transferSliders.Clear();
            }
            GUILayout.EndHorizontal();

            // Estimated cost
            if (hasTransfer)
            {
                double cost = electricCostBase + total * electricCostPerUnit;
                GUILayout.Label($"Estimated EC cost: <b>{cost:F2}</b>  (active vessel has {_availableEC:F1})");
            }
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_statusMessage)) return;
            _statusFadeTimer -= Time.deltaTime;
            if (_statusFadeTimer > 0f)
            {
                Color old = GUI.color;
                float alpha = Mathf.Clamp01(_statusFadeTimer / 3f);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUILayout.Label(_statusMessage);
                GUI.color = old;
            }
        }

        // ====================================================================
        // Data gathering
        // ====================================================================

        /// <summary>
        /// Rebuild the vessel list and resource summaries each GUI frame.
        /// This is cheap — IMGUI calls OnGUI multiple times per frame, but
        /// vessel iteration is fast.
        /// </summary>
        private void RebuildCache()
        {
            _vesselsInRange = ScanVesselsInRange();

            // Validate indices
            if (_sourceVesselIdx >= _vesselsInRange.Count) _sourceVesselIdx = -1;
            if (_destVesselIdx   >= _vesselsInRange.Count) _destVesselIdx   = -1;

            if (_sourceVesselIdx >= 0)
                _sourceResources = SummariseVesselResources(_vesselsInRange[_sourceVesselIdx]);
            else
                _sourceResources.Clear();

            if (_destVesselIdx >= 0)
                _destResources = SummariseVesselResources(_vesselsInRange[_destVesselIdx]);
            else
                _destResources.Clear();

            // EC available on active vessel
            _availableEC = 0.0;
            Vessel av = FlightGlobals.ActiveVessel;
            if (av != null)
                _availableEC = av.rootPart.Resources.Get(PartResourceLibrary.ElectricityHashcode)?.amount ?? 0.0;
        }

        /// <summary>
        /// Return all loaded vessels within maxTransferRange of the active vessel,
        /// with the active vessel first (so index 0 = self).
        /// </summary>
        private List<Vessel> ScanVesselsInRange()
        {
            var list = new List<Vessel>();
            Vessel active = FlightGlobals.ActiveVessel;
            if (active == null) return list;

            Vector3 pos = active.transform.position;

            // Always put active vessel first
            list.Add(active);

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == active) continue;
                if (!v.loaded) continue;       // only loaded (physics-range) vessels
                if (v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown)
                    continue;                  // skip asteroids, debris markers, etc.

                double dist = Vector3.Distance(pos, v.transform.position);
                if (dist <= (double)maxTransferRange)
                    list.Add(v);
            }
            return list;
        }

        /// <summary>
        /// Aggregate all PartResources across every part of a vessel into
        /// a list of VesselResourceSummary entries, one per distinct resource id.
        /// </summary>
        private List<VesselResourceSummary> SummariseVesselResources(Vessel v)
        {
            var dict = new Dictionary<int, VesselResourceSummary>();
            if (v == null || v.rootPart == null) return new List<VesselResourceSummary>();

            foreach (Part p in v.Parts)
            {
                if (p.Resources == null) continue;
                foreach (PartResource r in p.Resources)
                {
                    if (!dict.TryGetValue(r.info.id, out VesselResourceSummary sum))
                    {
                        sum = new VesselResourceSummary
                        {
                            Name      = r.resourceName,
                            Id        = r.info.id,
                            FlowState = r.flowState,
                        };
                        dict[r.info.id] = sum;
                    }
                    sum.Amount   += r.amount;
                    sum.Capacity += r.maxAmount;
                }
            }

            // Sort alphabetically by name
            var list = dict.Values.ToList();
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        // ====================================================================
        // Transfer execution
        // ====================================================================

        private void ExecuteTransfers()
        {
            if (_sourceVesselIdx < 0 || _destVesselIdx < 0) return;
            if (_sourceVesselIdx == _destVesselIdx) return;

            Vessel srcVessel = _vesselsInRange[_sourceVesselIdx];
            Vessel dstVessel = _vesselsInRange[_destVesselIdx];

            if (srcVessel == null || dstVessel == null)
            {
                SetStatus("<color=red>Source or destination vessel no longer exists.</color>");
                return;
            }

            // Sum the total units to transfer
            float totalUnits = _transferSliders.Values.Sum();
            if (totalUnits < 0.0001f)
            {
                SetStatus("<color=grey>Nothing to transfer.</color>");
                return;
            }

            // Check EC
            double ecCost = electricCostBase + totalUnits * electricCostPerUnit;
            if (_availableEC < ecCost - 0.0001)
            {
                SetStatus($"<color=orange>Not enough ElectricCharge. Need {ecCost:F1}, have {_availableEC:F1}.</color>");
                return;
            }

            // ---- Deduct EC from active vessel ----
            if (ecCost > 0.0)
            {
                double remaining = ecCost;
                Vessel active = FlightGlobals.ActiveVessel;
                if (active != null)
                {
                    foreach (Part p in active.Parts)
                    {
                        if (p.Resources == null) continue;
                        foreach (PartResource r in p.Resources)
                        {
                            if (r.info.id == PartResourceLibrary.ElectricityHashcode && r.amount > 0)
                            {
                                double take = Math.Min(r.amount, remaining);
                                r.amount -= take;
                                remaining -= take;
                                if (remaining < 0.0001) break;
                            }
                        }
                        if (remaining < 0.0001) break;
                    }
                }
                _availableEC -= ecCost - remaining;
            }

            // ---- Transfer each resource ----
            int transferredCount = 0;
            foreach (var kv in _transferSliders)
            {
                int resId = kv.Key;
                float amount = kv.Value;
                if (amount < 0.0001f) continue;

                double drained = DrainFromVessel(srcVessel, resId, amount);
                if (drained < 0.0001) continue;

                double filled = FillVessel(dstVessel, resId, drained);
                // Note: filled may be less than drained if destination somehow runs out of capacity.
                // Any "lost" resource is just destroyed (same behavior as stock transfer).

                if (filled > 0.0001)
                    transferredCount++;
            }

            // Clear sliders after capturing transfer info
            _transferSliders.Clear();

            if (transferredCount > 0)
                SetStatus($"<color=green>Transfer complete: {transferredCount} resource(s) moved from " +
                          $"<b>{srcVessel.vesselName}</b> → <b>{dstVessel.vesselName}</b>.  EC cost: {ecCost:F1}</color>");
            else
                SetStatus("<color=grey>No resources were transferred (destination may be full).</color>");
        }

        /// <summary>
        /// Remove `amount` units of resource `resId` from a vessel, spreading
        /// the drain proportionally across parts that contain it.
        /// Returns the amount actually drained.
        /// </summary>
        private double DrainFromVessel(Vessel v, int resId, double amount)
        {
            if (amount <= 0.0) return 0.0;

            // Build list of parts containing this resource with nonzero amount
            var parts = new List<PartResource>();
            foreach (Part p in v.Parts)
            {
                if (p.Resources == null) continue;
                foreach (PartResource r in p.Resources)
                {
                    if (r.info.id == resId && r.amount > 0.000001)
                        parts.Add(r);
                }
            }
            if (parts.Count == 0) return 0.0;

            double totalAvail = parts.Sum(r => r.amount);
            double toDrain = Math.Min(amount, totalAvail);
            double remaining = toDrain;

            // Drain equally (not proportionally by capacity — simpler, fairer)
            foreach (var r in parts)
            {
                double take = Math.Min(r.amount, remaining);
                r.amount -= take;
                remaining -= take;
                if (remaining < 0.000001) break;
            }

            return toDrain;
        }

        /// <summary>
        /// Add `amount` units of resource `resId` to a vessel, spreading
        /// the fill proportionally across parts that can hold more.
        /// Returns the amount actually filled.
        /// </summary>
        private double FillVessel(Vessel v, int resId, double amount)
        {
            if (amount <= 0.0) return 0.0;

            // Build list of parts containing this resource with spare capacity
            var parts = new List<PartResource>();
            foreach (Part p in v.Parts)
            {
                if (p.Resources == null) continue;
                foreach (PartResource r in p.Resources)
                {
                    if (r.info.id == resId && r.maxAmount - r.amount > 0.000001)
                        parts.Add(r);
                }
            }
            if (parts.Count == 0) return 0.0;

            double totalSpare = parts.Sum(r => r.maxAmount - r.amount);
            double toFill = Math.Min(amount, totalSpare);
            double remaining = toFill;

            // Fill equally across parts with spare capacity
            // Sort by spare capacity descending so larger tanks fill first
            parts.Sort((a, b) => (b.maxAmount - b.amount).CompareTo(a.maxAmount - a.amount));

            foreach (var r in parts)
            {
                double add = Math.Min(r.maxAmount - r.amount, remaining);
                r.amount += add;
                remaining -= add;
                if (remaining < 0.000001) break;
            }

            return toFill;
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusFadeTimer = 5f;
        }
    }
}
