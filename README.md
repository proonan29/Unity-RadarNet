# Unity-RadarNet

Radar network tracking simulation in Unity 6 (URP).

- **Real terrain**: 1000 × 1000 km around the Korean peninsula (land and sea, bathymetry) from AWS Terrain Tiles.
- **32 fictional radars** sited automatically on the terrain: long-range, phased-array early warning, medium-range,
  coastal, low-altitude gap fillers and ships — radar equation, Swerling 1 detection, 4/3-earth horizon and terrain masking.
- **Objects**: drones, helicopters, fighters, airliners, cruise missiles (terrain following), ballistic missiles.
- **Tracking over a data link**: each radar runs its own Kalman tracker; the radar that happens to pick up an object
  reports it over the link, other radars correlate it, are cued to it as it enters their coverage and keep the same
  system track number (STN). The control centre fuses reports (covariance intersection), tracks reporting
  responsibility (handover chain) and coasts tracks through coverage gaps.
- **Views**: 3D exaggerated terrain and a 2D tactical board, coverage overlay, data link message pulses, UI in Korean/English.
- **CLI**: everything can be driven from `unity command rn_*` (see `Docs/BuildFromScratch.md`, Korean).

Radar positions and all performance figures are generic illustrative values, not any real system or facility.
Terrain data: AWS Open Data Terrain Tiles (SRTM, GMTED2010, ETOPO1 and others).

Quick start: open the project with Unity 6000.6.3f1, open `Assets/RadarNet/Scenes/RadarNet.unity`, press Play.
