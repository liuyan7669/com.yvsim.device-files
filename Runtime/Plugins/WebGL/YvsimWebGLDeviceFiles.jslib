var YvsimWebGLDeviceFilesLibrary = {
  $YvsimWebGLDeviceFiles: {
    fileCallback: 0,
    selectionStateCallback: 0,
    pickerOpen: false,
    activeInput: null,
    objectUrls: {},
    textDecoder: null,
    textEncoder: null,

    decodeUtf8: function (bytes, byteLength) {
      if (byteLength < 0) {
        throw new RangeError('byteLength must not be negative.');
      }

      if (byteLength === 0) {
        return '';
      }

      if (!bytes) {
        throw new TypeError('bytes must not be null when byteLength is greater than zero.');
      }

      if (!YvsimWebGLDeviceFiles.textDecoder) {
        YvsimWebGLDeviceFiles.textDecoder = new TextDecoder('utf-8', { fatal: true });
      }

      return YvsimWebGLDeviceFiles.textDecoder.decode(
        HEAPU8.subarray(bytes, bytes + byteLength)
      );
    },

    allocateUtf8: function (value) {
      if (!YvsimWebGLDeviceFiles.textEncoder) {
        YvsimWebGLDeviceFiles.textEncoder = new TextEncoder();
      }

      var bytes = YvsimWebGLDeviceFiles.textEncoder.encode(value || '');
      var pointer = _malloc(Math.max(bytes.length, 1));
      if (!pointer) {
        throw new Error('Failed to allocate UTF-8 memory.');
      }

      if (bytes.length > 0) {
        HEAPU8.set(bytes, pointer);
      }

      return { pointer: pointer, length: bytes.length };
    },

    notifySelectionState: function (state, fileCount, message) {
      if (!YvsimWebGLDeviceFiles.selectionStateCallback) {
        return;
      }

      var encodedMessage = YvsimWebGLDeviceFiles.allocateUtf8(message || '');
      try {
        {{{ makeDynCall('viiii', 'YvsimWebGLDeviceFiles.selectionStateCallback') }}}(
          state,
          fileCount,
          encodedMessage.pointer,
          encodedMessage.length
        );
      } finally {
        _free(encodedMessage.pointer);
      }
    },

    cleanupPicker: function () {
      var input = YvsimWebGLDeviceFiles.activeInput;
      if (input && input.parentNode) {
        input.parentNode.removeChild(input);
      }

      YvsimWebGLDeviceFiles.activeInput = null;
      YvsimWebGLDeviceFiles.pickerOpen = false;
    },

    sendFile: function (file, dataBytes) {
      if (!YvsimWebGLDeviceFiles.fileCallback) {
        throw new Error('Unity file callback is not registered.');
      }

      var dataPointer = _malloc(Math.max(dataBytes.length, 1));
      if (!dataPointer) {
        throw new Error('Failed to allocate file memory.');
      }

      var encodedName = null;
      var encodedMime = null;
      var encodedObjectUrl = null;
      var objectUrl = '';

      try {
        if (dataBytes.length > 0) {
          HEAPU8.set(dataBytes, dataPointer);
        }

        objectUrl = URL.createObjectURL(file);
        YvsimWebGLDeviceFiles.objectUrls[objectUrl] = true;
        encodedName = YvsimWebGLDeviceFiles.allocateUtf8(file.name || 'unnamed');
        encodedMime = YvsimWebGLDeviceFiles.allocateUtf8(file.type || '');
        encodedObjectUrl = YvsimWebGLDeviceFiles.allocateUtf8(objectUrl);

        {{{ makeDynCall('viiiiiiii', 'YvsimWebGLDeviceFiles.fileCallback') }}}(
          dataPointer,
          dataBytes.length,
          encodedName.pointer,
          encodedName.length,
          encodedMime.pointer,
          encodedMime.length,
          encodedObjectUrl.pointer,
          encodedObjectUrl.length
        );
      } catch (error) {
        if (objectUrl) {
          URL.revokeObjectURL(objectUrl);
          delete YvsimWebGLDeviceFiles.objectUrls[objectUrl];
        }

        throw error;
      } finally {
        _free(dataPointer);
        if (encodedName) {
          _free(encodedName.pointer);
        }
        if (encodedMime) {
          _free(encodedMime.pointer);
        }
        if (encodedObjectUrl) {
          _free(encodedObjectUrl.pointer);
        }
      }
    },

    readFiles: function (files, index, maxFileBytes, completedCount) {
      if (index >= files.length) {
        YvsimWebGLDeviceFiles.cleanupPicker();
        YvsimWebGLDeviceFiles.notifySelectionState(0, completedCount, '');
        return;
      }

      var file = files[index];
      var absoluteLimit = 2147483647;
      if (file.size > absoluteLimit || (maxFileBytes > 0 && file.size > maxFileBytes)) {
        YvsimWebGLDeviceFiles.cleanupPicker();
        YvsimWebGLDeviceFiles.notifySelectionState(
          2,
          completedCount,
          'The selected file exceeds the configured size limit: ' + (file.name || 'unnamed')
        );
        return;
      }

      var reader = new FileReader();
      reader.onload = function () {
        try {
          var bytes = new Uint8Array(reader.result || new ArrayBuffer(0));
          YvsimWebGLDeviceFiles.sendFile(file, bytes);
          YvsimWebGLDeviceFiles.readFiles(
            files,
            index + 1,
            maxFileBytes,
            completedCount + 1
          );
        } catch (error) {
          YvsimWebGLDeviceFiles.cleanupPicker();
          YvsimWebGLDeviceFiles.notifySelectionState(2, completedCount, String(error));
        }
      };

      reader.onerror = function () {
        YvsimWebGLDeviceFiles.cleanupPicker();
        YvsimWebGLDeviceFiles.notifySelectionState(
          2,
          completedCount,
          'Failed to read the selected file: ' + (file.name || 'unnamed')
        );
      };

      reader.readAsArrayBuffer(file);
    }
  },

  YvsimWebGLFile_RegisterCallbacks__deps: ['$YvsimWebGLDeviceFiles'],
  YvsimWebGLFile_RegisterCallbacks: function (fileCallback, selectionStateCallback) {
    YvsimWebGLDeviceFiles.fileCallback = fileCallback;
    YvsimWebGLDeviceFiles.selectionStateCallback = selectionStateCallback;
  },

  YvsimWebGLFile_UnregisterCallbacks__deps: ['$YvsimWebGLDeviceFiles'],
  YvsimWebGLFile_UnregisterCallbacks: function () {
    YvsimWebGLDeviceFiles.fileCallback = 0;
    YvsimWebGLDeviceFiles.selectionStateCallback = 0;
    YvsimWebGLDeviceFiles.cleanupPicker();

    var urls = Object.keys(YvsimWebGLDeviceFiles.objectUrls);
    for (var i = 0; i < urls.length; i++) {
      URL.revokeObjectURL(urls[i]);
    }
    YvsimWebGLDeviceFiles.objectUrls = {};
  },

  YvsimWebGLFile_OpenPicker__deps: ['$YvsimWebGLDeviceFiles'],
  YvsimWebGLFile_OpenPicker: function (acceptBytes, acceptLength, allowMultiple, maxFileBytes) {
    if (YvsimWebGLDeviceFiles.pickerOpen || !YvsimWebGLDeviceFiles.fileCallback) {
      return 0;
    }

    var input = document.createElement('input');
    input.type = 'file';
    input.style.display = 'none';
    input.multiple = allowMultiple !== 0;

    try {
      var accept = YvsimWebGLDeviceFiles.decodeUtf8(acceptBytes, acceptLength);
      if (accept) {
        input.accept = accept;
      }

      YvsimWebGLDeviceFiles.pickerOpen = true;
      YvsimWebGLDeviceFiles.activeInput = input;
      document.body.appendChild(input);

      input.addEventListener('change', function () {
        if (!YvsimWebGLDeviceFiles.pickerOpen) {
          return;
        }

        var files = input.files ? Array.prototype.slice.call(input.files) : [];
        if (files.length === 0) {
          YvsimWebGLDeviceFiles.cleanupPicker();
          YvsimWebGLDeviceFiles.notifySelectionState(1, 0, '');
          return;
        }

        YvsimWebGLDeviceFiles.readFiles(files, 0, maxFileBytes, 0);
      }, { once: true });

      input.addEventListener('cancel', function () {
        if (!YvsimWebGLDeviceFiles.pickerOpen) {
          return;
        }

        YvsimWebGLDeviceFiles.cleanupPicker();
        YvsimWebGLDeviceFiles.notifySelectionState(1, 0, '');
      }, { once: true });

      input.click();
      return 1;
    } catch (error) {
      YvsimWebGLDeviceFiles.cleanupPicker();
      YvsimWebGLDeviceFiles.notifySelectionState(2, 0, String(error));
      return 0;
    }
  },

  YvsimWebGLFile_DownloadBytes__deps: ['$YvsimWebGLDeviceFiles'],
  YvsimWebGLFile_DownloadBytes: function (
    dataBytes,
    dataLength,
    nameBytes,
    nameLength,
    mimeBytes,
    mimeLength
  ) {
    try {
      if (dataLength < 0 || (dataLength > 0 && !dataBytes)) {
        return 0;
      }

      var fileName = YvsimWebGLDeviceFiles.decodeUtf8(nameBytes, nameLength);
      var mimeType = YvsimWebGLDeviceFiles.decodeUtf8(mimeBytes, mimeLength);
      var copiedBytes = dataLength > 0
        ? new Uint8Array(HEAPU8.subarray(dataBytes, dataBytes + dataLength))
        : new Uint8Array(0);
      var blob = new Blob([copiedBytes], {
        type: mimeType || 'application/octet-stream'
      });
      var objectUrl = URL.createObjectURL(blob);
      var anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = fileName || 'download';
      anchor.style.display = 'none';
      document.body.appendChild(anchor);
      anchor.click();
      document.body.removeChild(anchor);
      setTimeout(function () {
        URL.revokeObjectURL(objectUrl);
      }, 0);
      return 1;
    } catch (error) {
      console.error('YvsimWebGLDeviceFiles download failed.', error);
      return 0;
    }
  },

  YvsimWebGLFile_ReleaseObjectUrl__deps: ['$YvsimWebGLDeviceFiles'],
  YvsimWebGLFile_ReleaseObjectUrl: function (objectUrlBytes, objectUrlLength) {
    try {
      var objectUrl = YvsimWebGLDeviceFiles.decodeUtf8(objectUrlBytes, objectUrlLength);
      if (!Object.prototype.hasOwnProperty.call(YvsimWebGLDeviceFiles.objectUrls, objectUrl)) {
        return 0;
      }

      URL.revokeObjectURL(objectUrl);
      delete YvsimWebGLDeviceFiles.objectUrls[objectUrl];
      return 1;
    } catch (error) {
      console.error('YvsimWebGLDeviceFiles failed to release an object URL.', error);
      return 0;
    }
  }
};

mergeInto(LibraryManager.library, YvsimWebGLDeviceFilesLibrary);
