// Dynamic proxy configuration for .NET Aspire service discovery.
// When running under Aspire, the API endpoint is injected via the
// 'services__api__http__0' environment variable. Falls back to the
// Docker Compose hostname for other environments.
const apiTarget =
  process.env['services__api__http__0'] ||
  process.env['services__api__https__0'] ||
  'http://meta-anime-api:8080';

module.exports = {
  '/api': {
    target: apiTarget,
    secure: false,
    changeOrigin: true,
  },
  '/hubs': {
    target: apiTarget,
    secure: false,
    changeOrigin: true,
    ws: true,
  },
};
