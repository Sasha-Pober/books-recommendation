// Saves the API's OpenAPI document (the API must be running in Development) for `npm run api:types`.
import { writeFile } from 'node:fs/promises';

const url = process.env.API_URL ?? 'http://localhost:5292/openapi/v1.json';
const response = await fetch(url);
if (!response.ok) {
  throw new Error(`${url}: HTTP ${response.status}`);
}
await writeFile('openapi.json', JSON.stringify(await response.json(), null, 2) + '\n');
console.log(`Saved openapi.json from ${url}`);
