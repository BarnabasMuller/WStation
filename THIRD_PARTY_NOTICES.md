# Third-party components

WStation itself is licensed under the GNU General Public License v3.0 (see [LICENSE](LICENSE)).
It bundles the following third-party components, each under its own licence:

| Component | Version | Where | Licence |
|---|---|---|---|
| [Apache ECharts](https://echarts.apache.org/) | 5.5.1 | `web/echarts.min.js` (embedded into the Viewer) | Apache License 2.0 – <https://www.apache.org/licenses/LICENSE-2.0> |
| [Microsoft Edge WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2/) | 1.0.4258.31 | `lib/*.dll` (embedded into the Viewer, extracted at runtime) | BSD-style licence, see [lib/WebView2_LICENSE.txt](lib/WebView2_LICENSE.txt) |

Both licences are compatible with GPL-3.0 for distribution in combined form.

The Microsoft Edge WebView2 **Runtime** is not bundled; it ships with Windows 11 (and current Windows 10).

## Data and algorithms

- Weather data comes from the user's own station via the
  [Weather Underground PWS API](https://www.wunderground.com/member/api-keys) (an API key is required and is never stored in this repository).
- FAO-56 Penman–Monteith reference evapotranspiration and soil water balance: Allen, R. G. et al. (1998),
  *Crop evapotranspiration*, FAO Irrigation and Drainage Paper 56.
- Canadian Fire Weather Index: Van Wagner, C. E. (1987), *Development and structure of the Canadian Forest
  Fire Weather Index System*; equations as in the `cffdrs` reference implementation.
- SCS curve number runoff: USDA NRCS, National Engineering Handbook, Part 630.
- Zambretti forecaster: Negretti & Zambra (1915).
