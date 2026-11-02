/**
 * The address this phone's browser gave for notifications, or null when there is none (or looking takes too long). Signing out uses it to
 * stop notifications to this phone for the person who is leaving.
 */
export async function currentPushEndpoint(waitMs = 1500): Promise<string | null> {
  try {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) return null;
    const look = navigator.serviceWorker.getRegistration().then((registration) => registration?.pushManager.getSubscription() ?? null);
    const subscription = await Promise.race([look, new Promise<null>((resolve) => setTimeout(() => resolve(null), waitMs))]);
    return subscription?.endpoint ?? null;
  } catch {
    return null;
  }
}
