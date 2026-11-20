import { Component, ElementRef, NgZone, OnInit, ViewChild, input, computed, inject, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Map, NavigationControl, setWorkerUrl } from 'maplibre-gl';
import { FeatureCollection, Feature, Point } from 'geojson';

export interface CountryTraffic {
  countryCode: string; // "ES", "FI", "US"
  value: number;
}

export const COUNTRY_COORDS: Record<string, [number, number]> = {
  ES: [-3.7038, 40.4168],
  FI: [25.7482, 61.9241],
  US: [-98.5795, 39.8283],
  FR: [2.2137, 46.2276],
  DE: [10.4515, 51.1657],
  IT: [12.5674, 41.8719],
  PT: [-8.2245, 39.3999],
  UK: [-3.4359, 55.3781],
  CA: [-106.3468, 56.1304],
  MX: [-102.5528, 23.6345],
  BR: [-51.9253, -14.2350],
  AR: [-63.6167, -38.4161],
  CN: [104.1954, 35.8617],
  JP: [138.2529, 36.2048],
  AU: [133.7751, -25.2744],
  IN: [78.9629, 20.5937],
  RU: [105.3188, 61.5240],
  ZA: [22.9375, -30.5595],
  NG: [8.6753, 9.0820],
  EG: [30.8025, 26.8206],
  SA: [45.0792, 23.8859],
  TR: [35.2433, 38.9637],
  NL: [5.2913, 52.1326],
  BE: [4.4699, 50.5039],
  CH: [8.2275, 46.8182],
  SE: [18.6435, 60.1282],
  NO: [8.4689, 60.4720],
  DK: [9.5018, 56.2639],
  PL: [19.1451, 51.9194],
  IE: [-8.2439, 53.4129],
  CO: [-74.2973, 4.5709],
  CL: [-71.5430, -35.6751],
  PE: [-75.0152, -9.1900],
  VE: [-66.5897, 6.4238],
};

export const COUNTRY_NAMES: Record<string, string> = {
  ES: 'Spain',
  FI: 'Finland',
  US: 'United States',
  FR: 'France',
  DE: 'Germany',
  IT: 'Italy',
  PT: 'Portugal',
  UK: 'United Kingdom',
  CA: 'Canada',
  MX: 'Mexico',
  BR: 'Brazil',
  AR: 'Argentina',
  CN: 'China',
  JP: 'Japan',
  AU: 'Australia',
  IN: 'India',
  RU: 'Russia',
  ZA: 'South Africa',
  NG: 'Nigeria',
  EG: 'Egypt',
  SA: 'Saudi Arabia',
  TR: 'Turkey',
  NL: 'Netherlands',
  BE: 'Belgium',
  CH: 'Switzerland',
  SE: 'Sweden',
  NO: 'Norway',
  DK: 'Denmark',
  PL: 'Poland',
  IE: 'Ireland',
  CO: 'Colombia',
  CL: 'Chile',
  PE: 'Peru',
  VE: 'Venezuela',
};

@Component({
  selector: 'ta-location-map',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './location-map.html',
  styles: [`:host { display: block; height: 100%; }`]
})
export class LocationMapComponent {

  // Signal that indicates when the map is fully initialized and resized
  ready = signal(false);

  // Optional callback input that parent can pass to be notified when the map is ready
  onReady = input<(() => void) | undefined>(undefined);


  data = input<{ countryCode: string; value: number }[]>([]);
  simpleMode = input(false);
  mode = input<'points' | 'polygons'>('polygons');

  getFlagEmoji(countryCode: string): string {
    if (!countryCode || countryCode === 'unknown') return '🏳️';
    const codePoints = countryCode
      .toUpperCase()
      .split('')
      .map(char => 127397 + char.charCodeAt(0));
    return String.fromCodePoint(...codePoints);
  }

  containerClass = computed(() => this.simpleMode()
    ? 'relative h-full'
    : 'rounded-2xl bg-slate-900/40 shadow-[0_0_0_1px_rgba(255,255,255,0.03)] p-6 relative');

  mapClass = computed(() => this.simpleMode()
    ? 'w-full h-full rounded-xl overflow-hidden'
    : 'w-full h-80 rounded-xl overflow-hidden');

  @ViewChild('map') mapElement!: ElementRef<HTMLDivElement>;
  @ViewChild('tooltip') tooltip!: ElementRef<HTMLDivElement>;

  map!: Map;
  pulseValue = 0;

  private _onWindowResize: () => void = () => { };

  private zone = inject(NgZone);

  ngAfterViewInit() {
    // MapLibre 6 resolves its worker next to its own module, which the Angular bundle renames; angular.json copies the
    // worker to /assets/maplibre, so point MapLibre at it before the first map is created.
    setWorkerUrl(new URL('assets/maplibre/maplibre-gl-worker.mjs', document.baseURI).href);
    this.map = new Map({
      container: this.mapElement.nativeElement,
      style: 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json',
      center: [0, 20],
      zoom: 1.3,
    });

    this.map.addControl(new NavigationControl());

    this.map.on('load', () => {
      if (this.mode() === 'polygons') {
        this.addWorldPolygons();
      } else {
        this.addCountryPoints();
        this.addHeatmapLayer();
        this.addPulseLayer();
        this.startPulseAnimation();
      }
      this.setupZoomEvents();
      this.setupTooltipEvents();

      // Give the browser a tick to finish layout, then ensure the map sizes itself
      setTimeout(() => {
        try {
          this.map.resize();
        } catch (e) {
          // ignore
        }
        // mark ready after resize/layout settled
        this.ready.set(true);
        try {
          const cb = this.onReady?.();
          if (typeof cb === 'function') cb();
        } catch (_) {
          // ignore callback errors
        }
      }, 60);

      // Keep map responsive to window resizes
      this._onWindowResize = () => {
        try { this.map.resize(); } catch { }
      };
      window.addEventListener('resize', this._onWindowResize);
    });
  }


  ngOnDestroy(): void {
    try {
      window.removeEventListener('resize', this._onWindowResize);
    } catch { }
    try {
      this.map?.remove();
    } catch { }
  }
  private addCountryPoints() {
    const geojson = this.toGeoJSON(this.data());

    this.map.addSource('country-points', {
      type: 'geojson',
      data: geojson,
    });

    this.map.addLayer({
      id: 'country-circles',
      type: 'circle',
      source: 'country-points',
      paint: {
        'circle-radius': [
          'interpolate',
          ['linear'],
          ['get', 'value'],
          0, 4,
          2000, 10,
          5000, 16,
          10000, 20,
        ],
        'circle-color': '#38bdf8',
        'circle-opacity': 0.85,
      },
    });
  }

  private addHeatmapLayer() {
    this.map.addLayer({
      id: 'country-heatmap',
      type: 'heatmap',
      source: 'country-points',
      maxzoom: 5,
      paint: {
        'heatmap-weight': [
          'interpolate',
          ['linear'],
          ['get', 'value'],
          0, 0,
          2000, 0.3,
          5000, 0.6,
          10000, 1
        ],
        'heatmap-intensity': [
          'interpolate',
          ['linear'],
          ['zoom'],
          0, 0.8,
          5, 2
        ],
        'heatmap-color': [
          'interpolate',
          ['linear'],
          ['heatmap-density'],
          0.0, 'rgba(0, 0, 0, 0)',
          0.2, 'rgba(56, 189, 248, 0.2)',
          0.4, 'rgba(56, 189, 248, 0.4)',
          0.6, 'rgba(56, 189, 248, 0.6)',
          0.8, 'rgba(56, 189, 248, 0.8)',
          1.0, 'rgba(2, 132, 199, 1)'
        ],
        'heatmap-radius': [
          'interpolate',
          ['linear'],
          ['zoom'],
          0, 20,
          5, 50
        ],
        'heatmap-opacity': [
          'interpolate',
          ['linear'],
          ['zoom'],
          3, 0.7,
          6, 0.2
        ],
      }
    }, 'country-circles');
  }

  private addPulseLayer() {
    this.map.addLayer({
      id: 'country-pulse',
      type: 'circle',
      source: 'country-points',
      paint: {
        'circle-radius': [
          'interpolate',
          ['linear'],
          ['get', 'pulse'],
          0, 6,
          1, 30
        ],
        'circle-color': '#38bdf8',
        'circle-opacity': [
          'interpolate',
          ['linear'],
          ['get', 'pulse'],
          0, 0.35,
          1, 0
        ]
      }
    });
  }

  private startPulseAnimation() {
    this.zone.runOutsideAngular(() => {
      const animate = () => {
        this.pulseValue += 0.01;
        if (this.pulseValue > 1) this.pulseValue = 0;

        const source: any = this.map.getSource('country-points');
        if (source?._data?.features) {
          source._data.features.forEach((f: any) => (f.properties.pulse = this.pulseValue));
          source.setData(source._data);
        }

        requestAnimationFrame(animate);
      };
      animate();
    });
  }

  private addWorldPolygons() {
    // Load world countries GeoJSON
    this.map.addSource('world', {
      type: 'geojson',
      data: 'https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_110m_admin_0_countries.geojson'
    });

    // Prepare match expression for coloring
    const matchExpression: any[] = ['match', ['get', 'iso_a2']];

    // Add data to match expression
    this.data().forEach(item => {
      // Calculate color intensity based on value (simple logic for now)
      // Max value approx 10000 based on previous code
      const intensity = Math.min(item.value / 10000, 1);
      // Interpolate between transparent and blue
      // Using a fixed set of colors for simplicity or we can use rgba
      // Let's use 3 shades of blue
      let color = '#0ea5e9'; // sky-500 default
      if (item.value > 5000) color = '#0284c7'; // sky-600
      else if (item.value > 2000) color = '#38bdf8'; // sky-400
      else color = '#7dd3fc'; // sky-300

      matchExpression.push(item.countryCode, color);
    });

    // Default color for countries with no data
    matchExpression.push('rgba(0, 0, 0, 0)');

    this.map.addLayer({
      id: 'countries-fill',
      type: 'fill',
      source: 'world',
      paint: {
        'fill-color': matchExpression as any,
        'fill-opacity': 0.7,
        'fill-outline-color': '#1e293b' // slate-800
      }
    });

    // Add hover effect layer (optional, but nice)
    this.map.addLayer({
      id: 'countries-line',
      type: 'line',
      source: 'world',
      paint: {
        'line-color': '#334155',
        'line-width': 1
      }
    });
  }

  // ⭐ Dynamic tooltip
  private setupTooltipEvents() {
    const layerId = this.mode() === 'polygons' ? 'countries-fill' : 'country-circles';

    this.map.on('mousemove', layerId, (e: any) => {
      const feature = e.features?.[0];
      if (!feature) return;

      let countryName, value;

      if (this.mode() === 'polygons') {
        const iso = feature.properties.iso_a2;
        countryName = feature.properties.name || COUNTRY_NAMES[iso] || iso;
        const dataItem = this.data().find(d => d.countryCode === iso);
        value = dataItem ? dataItem.value : 0;
        if (value === 0) {
          this.tooltip.nativeElement.style.opacity = '0';
          this.map.getCanvas().style.cursor = '';
          return;
        }
      } else {
        const country = feature.properties.country;
        value = feature.properties.value;
        countryName = COUNTRY_NAMES[country] ?? country;
      }

      this.tooltip.nativeElement.innerHTML = `
      <div class="font-semibold">${countryName}</div>
      <div class="text-slate-300">${value} visits</div>
    `;

      this.tooltip.nativeElement.style.left = e.point.x + 'px';
      this.tooltip.nativeElement.style.top = e.point.y + 'px';
      this.tooltip.nativeElement.style.opacity = '1';

      this.map.getCanvas().style.cursor = 'pointer';
    });

    this.map.on('mouseleave', layerId, () => {
      this.tooltip.nativeElement.style.opacity = '0';
      this.map.getCanvas().style.cursor = '';
    });
  }

  // ⭐ Automatic zoom on click
  private setupZoomEvents() {
    const layerId = this.mode() === 'polygons' ? 'countries-fill' : 'country-circles';

    this.map.on('click', layerId, (e: any) => {
      const feature = e.features?.[0];
      if (!feature) return;

      let center;
      if (this.mode() === 'polygons') {
        center = e.lngLat;
      } else {
        center = feature.geometry.coordinates;
      }

      this.map.flyTo({
        center: center,
        zoom: 4,
        speed: 0.9,
        curve: 1.4,
      });
    });
  }

  private toGeoJSON(data: { countryCode: string; value: number }[]): FeatureCollection {
    return {
      type: 'FeatureCollection',
      features: data.map(item => ({
        type: 'Feature',
        properties: {
          value: item.value,
          country: item.countryCode,
        },
        pulse: 0,
        geometry: {
          type: 'Point',
          coordinates: COUNTRY_COORDS[item.countryCode] || [0, 0],
        },
      })) as Feature<Point>[],
    };
  }
}
