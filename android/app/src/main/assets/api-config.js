// Use the same IIS application path as index.html by default. For a separate API host,
// replace this with its HTTPS origin (and application path if applicable), no trailing slash.
// Example: window.FIXHANDS_API_BASE_URL = 'https://api.fixhands.com.ng';
window.FIXHANDS_API_BASE_URL = window.location.hostname === 'appassets.androidplatform.net'
  ? ''
  : window.location.origin + window.location.pathname.replace(/\/(?:index\.html)?$/i, '');
