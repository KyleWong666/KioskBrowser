package com.ssdc.kiosk.boot;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import com.ssdc.kiosk.KioskApp;
import com.ssdc.kiosk.MainActivity;

/** 开机自启（BOOT_COMPLETED 在后台启动 Activity 白名单内）。 */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) return;
        Log.i(KioskApp.TAG, "boot completed, launching main");
        Intent i = new Intent(context, MainActivity.class);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        context.startActivity(i);
    }
}
