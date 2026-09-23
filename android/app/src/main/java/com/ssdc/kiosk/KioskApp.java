package com.ssdc.kiosk;

import android.app.Application;
import android.util.Log;

public class KioskApp extends Application {
    public static final String TAG = "KioskBrowser";

    @Override
    public void onCreate() {
        super.onCreate();
        Log.i(TAG, "=== KioskBrowser starting ===");
    }
}
