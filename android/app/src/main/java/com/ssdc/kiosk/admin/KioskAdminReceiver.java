package com.ssdc.kiosk.admin;

import android.app.admin.DeviceAdminReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import com.ssdc.kiosk.KioskApp;

/**
 * Device Owner 接管点。授权方式（设备无账号登录状态下）：
 *   adb shell dpm set-device-owner com.ssdc.kiosk/.admin.KioskAdminReceiver
 * 有 root 的设备可直接执行。解除：adb shell dpm remove-active-admin --user current com.ssdc.kiosk/.admin.KioskAdminReceiver
 */
public class KioskAdminReceiver extends DeviceAdminReceiver {
    @Override
    public void onEnabled(Context context, Intent intent) {
        Log.i(KioskApp.TAG, "device admin enabled");
    }

    @Override
    public void onDisabled(Context context, Intent intent) {
        Log.w(KioskApp.TAG, "device admin disabled");
    }
}
