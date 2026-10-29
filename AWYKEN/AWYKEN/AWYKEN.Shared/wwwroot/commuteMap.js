// Loads Leaflet on demand and renders an OpenStreetMap-backed route for Commute.razor.
window.awykenCommuteMap = (() => {
    const maps = new Map();

    async function ensureLeaflet() {
        if (window.L) return;

        // Pin Leaflet and its integrity hashes so the map library is consistent in Web and MAUI.
        if (!document.getElementById("awyken-leaflet-css")) {
            const css = document.createElement("link");
            css.id = "awyken-leaflet-css";
            css.rel = "stylesheet";
            css.href = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
            css.integrity = "sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=";
            css.crossOrigin = "";
            document.head.appendChild(css);
        }

        await new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
            script.integrity = "sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=";
            script.crossOrigin = "";
            script.onload = resolve;
            script.onerror = () => reject(new Error("Leaflet failed to load"));
            document.head.appendChild(script);
        });
    }

    async function render(elementId, homeLat, homeLon, schoolLat, schoolLon, routePoints) {
        await ensureLeaflet();
        const element = document.getElementById(elementId);
        if (!element) return;

        let entry = maps.get(elementId);
        if (!entry) {
            // Start centered globally; saved endpoints will zoom the map when available.
            const map = L.map(elementId, { scrollWheelZoom: false }).setView([15, 0], 2);
            // Standard OSM tiles are fetched only for the visible map area and keep attribution visible.
            const tiles = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap contributors</a>'
            });
            tiles.addTo(map);
            entry = { map, homeMarker: null, schoolMarker: null, route: null };
            maps.set(elementId, entry);
        }

        const { map } = entry;
        if (entry.homeMarker) map.removeLayer(entry.homeMarker);
        if (entry.schoolMarker) map.removeLayer(entry.schoolMarker);
        if (entry.route) map.removeLayer(entry.route);

        const endpoints = [];
        if (Number.isFinite(homeLat) && Number.isFinite(homeLon)) {
            entry.homeMarker = L.marker([homeLat, homeLon]).bindPopup("Home").addTo(map);
            endpoints.push([homeLat, homeLon]);
        }
        if (Number.isFinite(schoolLat) && Number.isFinite(schoolLon)) {
            entry.schoolMarker = L.marker([schoolLat, schoolLon]).bindPopup("School / campus").addTo(map);
            endpoints.push([schoolLat, schoolLon]);
        }

        if (Array.isArray(routePoints) && routePoints.length > 1) {
            // Route points arrive as latitude/longitude pairs after the C# service converts OSRM order.
            entry.route = L.polyline(routePoints, { color: "#11194f", weight: 5, opacity: 0.85 }).addTo(map);
            map.fitBounds(entry.route.getBounds(), { padding: [24, 24] });
        } else if (endpoints.length === 2) {
            map.fitBounds(endpoints, { padding: [30, 30] });
        } else if (endpoints.length === 1) {
            map.setView(endpoints[0], 14);
        }

        // Leaflet may render before the Blazor layout settles, so recalculate after this frame.
        requestAnimationFrame(() => map.invalidateSize());
    }

    return { render };
})();
