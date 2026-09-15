/**
 * brainyPush.js — Web Push subscribe/unsubscribe helper for issue #315.
 *
 * Deliberately thin: it only talks to the browser's PushManager and hands the raw
 * subscription (endpoint + keys) back to Blazor, which does all persistence and business
 * logic via IPushSubscriptionService. This file never decides whether push is enabled for
 * the user (that is PushNotificationPreference.Enabled, server-side) — it only performs the
 * one-time browser permission/subscribe dance.
 */
window.brainyPush = (function () {
    function isSupported() {
        return 'serviceWorker' in navigator && 'PushManager' in window;
    }

    // Converts a URL-safe base64 VAPID public key into the Uint8Array PushManager expects.
    function urlBase64ToUint8Array(base64String) {
        const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
        const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        const rawData = atob(base64);
        const outputArray = new Uint8Array(rawData.length);
        for (let i = 0; i < rawData.length; i++) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    }

    function toDto(subscription) {
        const json = subscription.toJSON();
        return {
            endpoint: json.endpoint,
            p256dh: (json.keys && json.keys.p256dh) || '',
            auth: (json.keys && json.keys.auth) || ''
        };
    }

    async function getExistingSubscription() {
        if (!isSupported()) return null;
        const registration = await navigator.serviceWorker.ready;
        const subscription = await registration.pushManager.getSubscription();
        return subscription ? toDto(subscription) : null;
    }

    // Requests notification permission (if not already granted/denied) and subscribes.
    // Returns the subscription DTO Blazor forwards to IPushSubscriptionService.RegisterAsync,
    // or throws if the user denies permission or the browser has no push support.
    async function subscribe(vapidPublicKey) {
        if (!isSupported()) {
            throw new Error('Push notifications are not supported in this browser.');
        }
        if (!vapidPublicKey) {
            throw new Error('Push notifications are not configured on this server.');
        }

        const permission = await Notification.requestPermission();
        if (permission !== 'granted') {
            throw new Error('Notification permission was not granted.');
        }

        const registration = await navigator.serviceWorker.ready;
        let subscription = await registration.pushManager.getSubscription();
        if (!subscription) {
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(vapidPublicKey)
            });
        }

        return toDto(subscription);
    }

    // Unsubscribes this browser's own PushManager subscription. Blazor still separately
    // calls IPushSubscriptionService.UnregisterAsync to remove the server-side row — call
    // both so a stale subscription is never left registered on only one side.
    async function unsubscribe() {
        if (!isSupported()) return true;
        const registration = await navigator.serviceWorker.ready;
        const subscription = await registration.pushManager.getSubscription();
        if (subscription) {
            await subscription.unsubscribe();
        }
        return true;
    }

    // Best-effort, display-only device label (e.g. "Win32") shown next to a registered
    // subscription in the settings page. Never used for any security decision.
    function getDeviceLabel() {
        try {
            return (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || 'Browser';
        } catch {
            return 'Browser';
        }
    }

    return { isSupported, subscribe, unsubscribe, getExistingSubscription, getDeviceLabel };
})();
