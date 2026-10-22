import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { moveLegacyKeys } from './app/core/storage/legacy-keys';

moveLegacyKeys();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
