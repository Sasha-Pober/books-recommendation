# Book store frontend

Angular 22 SPA of the book store. See the repository README ("Frontend") for the architecture.

```bash
fnm use && npm ci
npm start              # http://localhost:4200 (proxies /api to the API on :5292)
npm test
npm run api:fetch && npm run api:types   # after API contract changes (API running in Development)
```
