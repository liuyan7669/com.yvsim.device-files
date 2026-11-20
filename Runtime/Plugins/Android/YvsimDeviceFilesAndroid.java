package com.yvsim.devicefiles;

import android.app.Activity;
import android.app.Fragment;
import android.content.ClipData;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.provider.OpenableColumns;

import com.unity3d.player.UnityPlayer;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

@SuppressWarnings("deprecation")
public final class YvsimDeviceFilesAndroid {
    private static final String FRAGMENT_TAG = "YvsimDeviceFiles.Fragment";
    private static final int OPEN_REQUEST_CODE = 48231;
    private static final int SAVE_REQUEST_CODE = 48232;
    private static final long MAX_MANAGED_BYTES = Integer.MAX_VALUE;
    private static final long STALE_SAVE_MILLISECONDS = 24L * 60L * 60L * 1000L;
    private static final ConcurrentLinkedQueue<String> EVENTS = new ConcurrentLinkedQueue<>();
    private static final ExecutorService IO_EXECUTOR = Executors.newSingleThreadExecutor();

    private YvsimDeviceFilesAndroid() {
    }

    public static String initialize(Activity activity) throws Exception {
        if (activity == null) {
            throw new IllegalStateException("Unity activity is unavailable.");
        }

        File root = getManagedRoot(activity);
        if (!root.exists() && !root.mkdirs()) {
            throw new IllegalStateException("Failed to create the Android file cache directory.");
        }

        cleanupStaleSaveFiles(activity);
        return root.getCanonicalPath();
    }

    public static boolean deleteManagedSelectedFile(Activity activity, String path) {
        return deleteManagedFile(activity, path, "Selected");
    }

    public static boolean deleteManagedSaveFile(Activity activity, String path) {
        return deleteManagedFile(activity, path, "Save");
    }

    public static void openFilePicker(
        final Activity activity,
        final String callbackObject,
        final String requestId,
        final String mimeTypesJson,
        final boolean allowMultiple,
        final long maxFileBytes) {
        if (activity == null) {
            enqueueFailure("selectionFailed", callbackObject, requestId, "Unity activity is unavailable.");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    getOrCreateFragment(activity).openFilePicker(
                        callbackObject,
                        requestId,
                        parseMimeTypes(mimeTypesJson),
                        allowMultiple,
                        maxFileBytes);
                } catch (Exception exception) {
                    enqueueFailure("selectionFailed", callbackObject, requestId, exception.toString());
                }
            }
        });
    }

    public static void saveFile(
        final Activity activity,
        final String callbackObject,
        final String requestId,
        final String sourcePath,
        final String fileName,
        final String mimeType) {
        if (activity == null) {
            enqueueFailure("saveFailed", callbackObject, requestId, "Unity activity is unavailable.");
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    getOrCreateFragment(activity).saveFile(
                        callbackObject,
                        requestId,
                        sourcePath,
                        fileName,
                        mimeType);
                } catch (Exception exception) {
                    deleteManagedSaveFile(activity, sourcePath);
                    enqueueFailure("saveFailed", callbackObject, requestId, exception.toString());
                }
            }
        });
    }

    public static String pollEvent() {
        String value = EVENTS.poll();
        return value == null ? "" : value;
    }

    private static BridgeFragment getOrCreateFragment(Activity activity) {
        Fragment existing = activity.getFragmentManager().findFragmentByTag(FRAGMENT_TAG);
        if (existing instanceof BridgeFragment) {
            return (BridgeFragment)existing;
        }

        BridgeFragment fragment = new BridgeFragment();
        activity.getFragmentManager()
            .beginTransaction()
            .add(fragment, FRAGMENT_TAG)
            .commitAllowingStateLoss();
        activity.getFragmentManager().executePendingTransactions();
        return fragment;
    }

    private static String[] parseMimeTypes(String json) {
        try {
            JSONArray values = new JSONArray(json == null ? "[]" : json);
            List<String> result = new ArrayList<>();
            for (int i = 0; i < values.length(); i++) {
                String value = values.optString(i, "").trim();
                if (value.contains("/") && !result.contains(value)) {
                    result.add(value);
                }
            }

            return result.toArray(new String[0]);
        } catch (Exception ignored) {
            return new String[0];
        }
    }

    private static String choosePrimaryMimeType(String[] mimeTypes) {
        if (mimeTypes.length == 1) {
            return mimeTypes[0];
        }

        return "*/*";
    }

    private static void enqueue(JSONObject value, String callbackObject) {
        EVENTS.add(value.toString());
        UnityPlayer.UnitySendMessage(callbackObject, "OnNativeEventsAvailable", "");
    }

    private static void enqueueSimple(String kind, String callbackObject, String requestId) {
        try {
            JSONObject value = new JSONObject();
            value.put("kind", kind);
            value.put("requestId", requestId == null ? "" : requestId);
            enqueue(value, callbackObject);
        } catch (Exception exception) {
            enqueueFallback(kind, callbackObject, requestId, exception.toString());
        }
    }

    private static void enqueueFailure(String kind, String callbackObject, String requestId, String message) {
        try {
            JSONObject value = new JSONObject();
            value.put("kind", kind);
            value.put("requestId", requestId == null ? "" : requestId);
            value.put("message", message == null ? "Unknown Android file error." : message);
            enqueue(value, callbackObject);
        } catch (Exception exception) {
            enqueueFallback(kind, callbackObject, requestId, exception.toString());
        }
    }

    private static void enqueueFallback(String kind, String callbackObject, String requestId, String message) {
        EVENTS.add("{\"kind\":\"" + kind + "\",\"requestId\":\"" +
            (requestId == null ? "" : requestId) +
            "\",\"message\":\"Android file operation failed.\"}");
        UnityPlayer.UnitySendMessage(callbackObject, "OnNativeEventsAvailable", message == null ? "" : message);
    }

    private static void enqueueSelected(String callbackObject, String requestId, CachedFile file) throws Exception {
        JSONObject value = new JSONObject();
        value.put("kind", "fileSelected");
        value.put("requestId", requestId);
        value.put("name", file.name);
        value.put("mimeType", file.mimeType);
        value.put("path", file.path);
        value.put("size", file.size);
        enqueue(value, callbackObject);
    }

    private static void enqueueSelectionCompleted(String callbackObject, String requestId, int count) throws Exception {
        JSONObject value = new JSONObject();
        value.put("kind", "selectionCompleted");
        value.put("requestId", requestId);
        value.put("count", count);
        enqueue(value, callbackObject);
    }

    private static void enqueueSaveCompleted(String callbackObject, String requestId, String uri) throws Exception {
        JSONObject value = new JSONObject();
        value.put("kind", "saveCompleted");
        value.put("requestId", requestId);
        value.put("uri", uri == null ? "" : uri);
        enqueue(value, callbackObject);
    }

    public static final class BridgeFragment extends Fragment {
        private String selectionCallbackObject;
        private String selectionRequestId;
        private long selectionMaximumBytes;
        private String saveCallbackObject;
        private String saveRequestId;
        private String saveSourcePath;
        private boolean dialogOpen;

        @Override
        public void onCreate(Bundle savedInstanceState) {
            super.onCreate(savedInstanceState);
            setRetainInstance(true);
        }

        void openFilePicker(
            String callbackObject,
            String requestId,
            String[] mimeTypes,
            boolean allowMultiple,
            long maxFileBytes) {
            if (dialogOpen) {
                enqueueFailure(
                    "selectionFailed",
                    callbackObject,
                    requestId,
                    "Another Android file dialog is already open.");
                return;
            }

            selectionCallbackObject = callbackObject;
            selectionRequestId = requestId;
            selectionMaximumBytes = Math.max(0L, maxFileBytes);
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType(choosePrimaryMimeType(mimeTypes));
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, allowMultiple);
            if (mimeTypes.length > 1) {
                intent.putExtra(Intent.EXTRA_MIME_TYPES, mimeTypes);
            }

            try {
                dialogOpen = true;
                startActivityForResult(intent, OPEN_REQUEST_CODE);
            } catch (Exception exception) {
                dialogOpen = false;
                selectionCallbackObject = null;
                selectionRequestId = null;
                selectionMaximumBytes = 0L;
                enqueueFailure("selectionFailed", callbackObject, requestId, exception.toString());
            }
        }

        void saveFile(
            String callbackObject,
            String requestId,
            String sourcePath,
            String fileName,
            String mimeType) throws Exception {
            if (dialogOpen) {
                Activity currentActivity = getActivity();
                deleteManagedSaveFile(currentActivity, sourcePath);
                enqueueFailure(
                    "saveFailed",
                    callbackObject,
                    requestId,
                    "Another Android file dialog is already open.");
                return;
            }

            Activity activity = getActivity();
            if (activity == null) {
                throw new IllegalStateException("Unity activity is unavailable.");
            }

            File source = new File(sourcePath).getCanonicalFile();
            File saveRoot = new File(getManagedRoot(activity), "Save").getCanonicalFile();
            String rootPrefix = saveRoot.getPath() + File.separator;
            if (!source.getPath().startsWith(rootPrefix) || !source.isFile()) {
                throw new SecurityException("The Android save source is outside the managed cache directory.");
            }

            saveCallbackObject = callbackObject;
            saveRequestId = requestId;
            saveSourcePath = source.getPath();
            Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType(mimeType != null && mimeType.contains("/")
                ? mimeType
                : "application/octet-stream");
            intent.putExtra(Intent.EXTRA_TITLE, fileName);
            intent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);

            try {
                dialogOpen = true;
                startActivityForResult(intent, SAVE_REQUEST_CODE);
            } catch (Exception exception) {
                dialogOpen = false;
                deleteManagedSaveFile(activity, saveSourcePath);
                saveCallbackObject = null;
                saveRequestId = null;
                saveSourcePath = null;
                enqueueFailure("saveFailed", callbackObject, requestId, exception.toString());
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            dialogOpen = false;

            if (requestCode == OPEN_REQUEST_CODE) {
                handleOpenResult(resultCode, data);
            } else if (requestCode == SAVE_REQUEST_CODE) {
                handleSaveResult(resultCode, data);
            }
        }

        private void handleOpenResult(int resultCode, Intent data) {
            final String callbackObject = selectionCallbackObject;
            final String requestId = selectionRequestId;
            final long maximumBytes = selectionMaximumBytes;
            selectionCallbackObject = null;
            selectionRequestId = null;
            selectionMaximumBytes = 0L;

            if (resultCode == Activity.RESULT_CANCELED) {
                enqueueSimple("selectionCanceled", callbackObject, requestId);
                return;
            }

            if (resultCode != Activity.RESULT_OK) {
                enqueueFailure(
                    "selectionFailed",
                    callbackObject,
                    requestId,
                    "Android file selection returned result code " + resultCode + ".");
                return;
            }

            if (data == null) {
                enqueueFailure(
                    "selectionFailed",
                    callbackObject,
                    requestId,
                    "Android returned no file selection result.");
                return;
            }

            final List<Uri> uris = collectUris(data);
            if (uris.isEmpty()) {
                enqueueFailure(
                    "selectionFailed",
                    callbackObject,
                    requestId,
                    "Android returned no selected document URI.");
                return;
            }

            final Activity activity = getActivity();
            if (activity == null) {
                enqueueFailure("selectionFailed", callbackObject, requestId, "Unity activity is unavailable.");
                return;
            }

            IO_EXECUTOR.execute(new Runnable() {
                @Override
                public void run() {
                    int completedCount = 0;
                    try {
                        for (Uri uri : uris) {
                            CachedFile file = cacheSelectedDocument(activity, uri, maximumBytes);
                            enqueueSelected(callbackObject, requestId, file);
                            completedCount++;
                        }

                        enqueueSelectionCompleted(callbackObject, requestId, completedCount);
                    } catch (Exception exception) {
                        enqueueFailure("selectionFailed", callbackObject, requestId, exception.toString());
                    }
                }
            });
        }

        private void handleSaveResult(int resultCode, Intent data) {
            final String callbackObject = saveCallbackObject;
            final String requestId = saveRequestId;
            final String sourcePath = saveSourcePath;
            saveCallbackObject = null;
            saveRequestId = null;
            saveSourcePath = null;

            final Activity activity = getActivity();
            if (resultCode == Activity.RESULT_CANCELED) {
                deleteManagedSaveFile(activity, sourcePath);
                enqueueSimple("saveCanceled", callbackObject, requestId);
                return;
            }

            if (resultCode != Activity.RESULT_OK) {
                deleteManagedSaveFile(activity, sourcePath);
                enqueueFailure(
                    "saveFailed",
                    callbackObject,
                    requestId,
                    "Android file save returned result code " + resultCode + ".");
                return;
            }

            if (data == null || data.getData() == null) {
                deleteManagedSaveFile(activity, sourcePath);
                enqueueFailure(
                    "saveFailed",
                    callbackObject,
                    requestId,
                    "Android returned no save destination URI.");
                return;
            }

            final Uri destination = data.getData();
            if (activity == null) {
                enqueueFailure("saveFailed", callbackObject, requestId, "Unity activity is unavailable.");
                return;
            }

            IO_EXECUTOR.execute(new Runnable() {
                @Override
                public void run() {
                    try {
                        copyToDestination(activity, sourcePath, destination);
                        enqueueSaveCompleted(callbackObject, requestId, destination.toString());
                    } catch (Exception exception) {
                        boolean destinationRemoved = deleteDestinationQuietly(activity, destination);
                        String message = exception.toString();
                        if (!destinationRemoved) {
                            message += " The document provider may have retained a partial file.";
                        }

                        enqueueFailure("saveFailed", callbackObject, requestId, message);
                    } finally {
                        deleteManagedSaveFile(activity, sourcePath);
                    }
                }
            });
        }

        private static List<Uri> collectUris(Intent data) {
            List<Uri> values = new ArrayList<>();
            ClipData clipData = data.getClipData();
            if (clipData != null) {
                for (int i = 0; i < clipData.getItemCount(); i++) {
                    Uri uri = clipData.getItemAt(i).getUri();
                    if (uri != null && !values.contains(uri)) {
                        values.add(uri);
                    }
                }
            } else if (data.getData() != null) {
                values.add(data.getData());
            }

            return values;
        }

        private static CachedFile cacheSelectedDocument(Activity activity, Uri uri, long configuredMaximum)
            throws Exception {
            if (!ContentResolver.SCHEME_CONTENT.equals(uri.getScheme())) {
                throw new SecurityException("Android file selection returned a non-content URI.");
            }

            ContentResolver resolver = activity.getContentResolver();
            DocumentMetadata metadata = queryMetadata(resolver, uri);
            if (metadata.size > MAX_MANAGED_BYTES ||
                (configuredMaximum > 0 && metadata.size > configuredMaximum)) {
                throw new IllegalArgumentException("The selected file exceeds the configured size limit: " + metadata.name);
            }

            File root = new File(getManagedRoot(activity), "Selected");
            if (!root.exists() && !root.mkdirs()) {
                throw new IllegalStateException("Failed to create the Android file cache directory.");
            }

            String extension = safeExtension(metadata.name);
            File output = new File(root, UUID.randomUUID().toString().replace("-", "") + extension);
            File partial = new File(output.getPath() + ".part");
            long copiedBytes = 0L;

            try (InputStream input = resolver.openInputStream(uri);
                 OutputStream outputStream = new FileOutputStream(partial)) {
                if (input == null) {
                    throw new IllegalStateException("Android could not open the selected document.");
                }

                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.read(buffer)) >= 0) {
                    if (read == 0) {
                        continue;
                    }

                    copiedBytes += read;
                    if (copiedBytes > MAX_MANAGED_BYTES ||
                        (configuredMaximum > 0 && copiedBytes > configuredMaximum)) {
                        throw new IllegalArgumentException(
                            "The selected file exceeds the configured size limit: " + metadata.name);
                    }

                    outputStream.write(buffer, 0, read);
                }

                outputStream.flush();
            } catch (Exception exception) {
                partial.delete();
                output.delete();
                throw exception;
            }

            if (!partial.renameTo(output)) {
                partial.delete();
                throw new IllegalStateException("Failed to finalize the Android cached file.");
            }

            return new CachedFile(
                metadata.name,
                metadata.mimeType,
                output.getCanonicalPath(),
                copiedBytes);
        }

        private static DocumentMetadata queryMetadata(ContentResolver resolver, Uri uri) {
            String name = "unnamed";
            long size = -1L;
            try (Cursor cursor = resolver.query(uri, null, null, null, null)) {
                if (cursor != null && cursor.moveToFirst()) {
                    int nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    int sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE);
                    if (nameIndex >= 0 && !cursor.isNull(nameIndex)) {
                        String candidate = cursor.getString(nameIndex);
                        if (candidate != null && candidate.length() > 0) {
                            name = candidate;
                        }
                    }

                    if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) {
                        size = cursor.getLong(sizeIndex);
                    }
                }
            } catch (Exception ignored) {
            }

            String mimeType = resolver.getType(uri);
            return new DocumentMetadata(name, mimeType == null ? "" : mimeType, size);
        }

        private static String safeExtension(String name) {
            int dotIndex = name == null ? -1 : name.lastIndexOf('.');
            if (dotIndex < 0 || dotIndex == name.length() - 1) {
                return "";
            }

            String value = name.substring(dotIndex);
            if (value.length() > 20) {
                return "";
            }

            for (int i = 1; i < value.length(); i++) {
                char character = value.charAt(i);
                if (!Character.isLetterOrDigit(character) && character != '_' && character != '-' && character != '+') {
                    return "";
                }
            }

            return value.toLowerCase();
        }

        private static void copyToDestination(Activity activity, String sourcePath, Uri destination)
            throws Exception {
            File source = new File(sourcePath);
            try (InputStream input = new FileInputStream(source);
                 OutputStream output = activity.getContentResolver().openOutputStream(destination, "w")) {
                if (output == null) {
                    throw new IllegalStateException("Android could not open the selected save destination.");
                }

                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.read(buffer)) >= 0) {
                    if (read > 0) {
                        output.write(buffer, 0, read);
                    }
                }

                output.flush();
            }
        }
    }

    private static File getManagedRoot(Activity activity) throws Exception {
        if (activity == null) {
            throw new IllegalStateException("Unity activity is unavailable.");
        }

        return new File(activity.getCacheDir(), "YvsimDeviceFiles").getCanonicalFile();
    }

    private static boolean deleteManagedFile(Activity activity, String path, String directoryName) {
        if (activity == null || path == null || path.length() == 0) {
            return false;
        }

        try {
            File root = new File(getManagedRoot(activity), directoryName).getCanonicalFile();
            File target = new File(path).getCanonicalFile();
            String prefix = root.getPath() + File.separator;
            if (!target.getPath().startsWith(prefix)) {
                return false;
            }

            return !target.exists() || (target.isFile() && target.delete());
        } catch (Exception ignored) {
            return false;
        }
    }

    private static void cleanupStaleSaveFiles(Activity activity) {
        try {
            File saveRoot = new File(getManagedRoot(activity), "Save").getCanonicalFile();
            File[] files = saveRoot.listFiles();
            if (files == null) {
                return;
            }

            long staleBefore = System.currentTimeMillis() - STALE_SAVE_MILLISECONDS;
            for (File file : files) {
                if (file.isFile() && file.getName().endsWith(".tmp") && file.lastModified() < staleBefore) {
                    deleteManagedSaveFile(activity, file.getPath());
                }
            }
        } catch (Exception ignored) {
        }
    }

    private static boolean deleteDestinationQuietly(Activity activity, Uri destination) {
        if (activity == null || destination == null) {
            return false;
        }

        try {
            return DocumentsContract.deleteDocument(activity.getContentResolver(), destination);
        } catch (Exception ignored) {
            return false;
        }
    }

    private static final class DocumentMetadata {
        final String name;
        final String mimeType;
        final long size;

        DocumentMetadata(String name, String mimeType, long size) {
            this.name = name;
            this.mimeType = mimeType;
            this.size = size;
        }
    }

    private static final class CachedFile {
        final String name;
        final String mimeType;
        final String path;
        final long size;

        CachedFile(String name, String mimeType, String path, long size) {
            this.name = name;
            this.mimeType = mimeType;
            this.path = path;
            this.size = size;
        }
    }
}
