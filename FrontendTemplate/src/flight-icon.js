// Copyright 2025 Kyant. Apache-2.0. Ported from FlightIcon.kt to SVG/Path2D.
// The original 960 x 960 path and 28dp display size are retained.
export const FLIGHT_PATH = 'M400 552L147 653q-24 10-45.5-4.5T80 608v-22q0-12 5.5-23t15.5-18l299-209V160q0-33 23.5-56.5T480 80q33 0 56.5 23.5T560 160v176l299 209q10 7 15.5 18t5.5 23v22q0 26-21.5 40.5T813 653L560 552v144l103 72q8 6 12.5 14.5T680 801v24q0 20-16.5 32.5T627 864l-147-44-147 44q-20 6-36.5-6.5T280 825v-24q0-10 4.5-18.5T297 768l103-72V552Z';

export const flightSvg = `<svg viewBox="0 0 960 960" width="28" height="28" aria-hidden="true" focusable="false"><path fill="currentColor" d="${FLIGHT_PATH}" /></svg>`;
