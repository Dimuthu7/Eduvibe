# Performance check on a mid-range phone

Checked 15 Oct 2026 for S9-5. The goal: a class screen in under 2 seconds, and attendance for a class of 100 students done in under 30 seconds.

## How

A production build of the web app (`npm run build`), gzip on, served from a static server; the API and PostgreSQL ran on the same machine. Headless Chromium with a 393 px phone viewport and touch, 4x CPU slowdown (about a mid-range phone) and a slow 4G network (1.6 Mbps, 150 ms delay, the setting Lighthouse uses for mobile). One teacher with a class of 100 students, signed in fresh with an empty cache. The run was a short throwaway Playwright script; repeat the same steps after big UI changes.

## Results (seconds unless stated)

| Step | 4x CPU, slow 4G | 6x CPU, slow 4G | 4x CPU, 4G (9 Mbps) |
|---|---|---|---|
| First visit: open the app to the sign-in form (nothing cached) | 3.4 | 3.8 | 1.8 |
| Sign in to the home screen | 1.0 | 1.5 | 1.2 |
| Reload to the class list, open a class | 2.0 | 2.4 | 1.9 |
| Reload the class screen (assets cached) | 0.9 | 1.3 | 0.8 |
| Attendance day list | 0.7 | 1.1 | 0.8 |
| Open a class of 100, all 100 shown | 0.6 | 0.7 | 0.6 |
| A tap to mark absent shows (milliseconds) | 140 to 170 | 180 to 230 | 145 to 220 |
| Save the marks | 0.5 | 0.7 | 0.5 |
| Whole job: home, open class, mark 5 absent, saved | 3.2 | 4.8 | 3.6 |

The first-visit app download is about 130 KB compressed.

## Reading it

- The class screen is under 2 seconds on every setting once the app is cached, and about 2 seconds when the page is reloaded from nothing but the list. On a 4x slow phone with 4G it takes 1.9 s.
- A class of 100 students is marked in a few seconds of work. The 30 second target is met many times over; the person's own tapping is the slow part.
- These numbers leave out the internet round trip to the API. On the free test deployment the first request after a quiet period takes about a minute while Render wakes the API. That is the free plan's limit, not the app's, and goes away on a paid plan.

## Not checked

A real phone. The numbers come from throttled desktop Chromium and should be confirmed on a real mid-range Android before the pilot.
