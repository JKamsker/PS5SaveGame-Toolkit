import { init } from './redoc/redoc.standalone.js';

// The pinned renderer styles html.light/html.dark; DocFX uses data-bs-theme.
// Keep one site-wide theme control instead of two independent preferences.
const root = document.documentElement;
const syncTheme = () => {
  const dark = root.dataset.bsTheme === 'dark';
  if (root.classList.contains('dark') !== dark) root.classList.toggle('dark', dark);
  if (root.classList.contains('light') === dark) root.classList.toggle('light', !dark);
};
syncTheme();
new MutationObserver(syncTheme).observe(root, {
  attributes: true,
  attributeFilter: ['data-bs-theme', 'class'],
});

// Use Redoc's supported initialization API so its sticky sidebar and operation
// deep links account for the actual DocFX navbar, including mobile layout.
init(new URL('../reference/openapi.json', import.meta.url).href, {
  disableTelemetry: true,
  layout: 'stacked',
  router: 'hash',
  schemaDefinitionsTagName: 'Schemas',
  sortRequiredPropsFirst: true,
  scrollYOffset: () => document.querySelector('header')?.getBoundingClientRect().height ?? 0,
}, document.getElementById('http-reference'));
