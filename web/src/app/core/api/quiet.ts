import { HttpContext, HttpContextToken } from '@angular/common/http';

/** Marks a background request (checking for new notifications): no loading bar, and no toast when it fails. The next check simply tries again. */
export const QUIET = new HttpContextToken<boolean>(() => false);

export const quiet = (): HttpContext => new HttpContext().set(QUIET, true);
