import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Marks a request the screen handles itself: no loading bar, and no toast when it fails (the notification check in the background, each dashboard widget with its own retry). */
export const QUIET = new HttpContextToken<boolean>(() => false);

export const quiet = (): HttpContext => new HttpContext().set(QUIET, true);
