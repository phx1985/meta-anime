/* ═══════════════════════════════════════════════════════════════════════════
 * VERIFIED HandBrake queue-job baseline — do not hand-edit key structure.
 *
 * Source: a real queue export from HandBrake GTK (fr.handbrake.ghb). The GTK UI
 * expects a FULL state snapshot and segfaults on unmapped controls, so every
 * key present here must stay present. Per-episode jobs are produced by
 * structuredClone(HANDBRAKE_JOB_TEMPLATE) and mutating ONLY the dynamic paths
 * (see buildHandBrakeQueue in utils/user-path.utils.ts).
 *
 * Personal paths in this snapshot are placeholders; all of them are overwritten
 * by the per-file mutation map. Regenerate from a fresh HandBrake queue export
 * if a future HandBrake version adds UI state keys.
 * ═══════════════════════════════════════════════════════════════════════════ */

export interface HandBrakeQueueEntry {
  Job: Record<string, unknown>;
  Title: Record<string, unknown>;
  uiSettings: Record<string, unknown>;
}

export const HANDBRAKE_JOB_TEMPLATE: HandBrakeQueueEntry = {
  "Job": {
    "Audio": {
      "AudioList": [
        {
          "Bitrate": 0,
          "DRC": 0.0,
          "Encoder": "copy:aac",
          "Mixdown": "none",
          "PresetEncoder": "copy",
          "Quality": -3.0,
          "Samplerate": 0,
          "Track": 0
        }
      ],
      "CopyMask": [
        "copy:mp3",
        "copy:aac"
      ],
      "FallbackEncoder": "av_aac"
    },
    "Destination": {
      "AlignAVStart": false,
      "ChapterList": [
        {
          "Duration": {
            "Hours": 0,
            "Minutes": 22,
            "Seconds": 10,
            "Ticks": 119722140
          },
          "Name": "Chapter 1"
        },
        {
          "Duration": {
            "Hours": 0,
            "Minutes": 1,
            "Seconds": 28,
            "Ticks": 7897860
          },
          "Name": "Ending"
        },
        {
          "Duration": {
            "Hours": 0,
            "Minutes": 0,
            "Seconds": 11,
            "Ticks": 1021860
          },
          "Name": "Chapter 3"
        }
      ],
      "ChapterMarkers": true,
      "File": "/output/destination.h265.mkv",
      "InlineParameterSets": false,
      "Mux": "mkv",
      "Options": {
        "IpodAtom": false,
        "Optimize": false
      }
    },
    "Filters": {
      "FilterList": [
        {
          "ID": 11,
          "Settings": {
            "mode": 0
          }
        },
        {
          "ID": 20,
          "Settings": {
            "crop-bottom": 0,
            "crop-left": 0,
            "crop-right": 0,
            "crop-top": 0,
            "height": 1080,
            "width": 1920
          }
        }
      ]
    },
    "Metadata": {
      "CreationTime": "2026-01-01T00:00:00.000000Z",
      "Name": "Series S01E001 Episode Title"
    },
    "PAR": {
      "Den": 1,
      "Num": 1
    },
    "SequenceID": 0,
    "Source": {
      "Angle": 0,
      "HWDecode": 0,
      "KeepDuplicateTitles": false,
      "Path": "/input/source.mkv",
      "Range": {
        "End": 3,
        "Start": 1,
        "Type": "chapter"
      },
      "Title": 1
    },
    "Subtitle": {
      "Search": {
        "Burn": true,
        "Default": false,
        "Enable": false,
        "Forced": false
      },
      "SubtitleList": [
        {
          "Burn": false,
          "Default": true,
          "Forced": false,
          "Track": 0
        }
      ]
    },
    "Video": {
      "AdapterIndex": -1,
      "AsyncDepth": 0,
      "ChromaLocation": 1,
      "ColorInputFormat": 0,
      "ColorMatrix": 1,
      "ColorOutputFormat": 0,
      "ColorPrimaries": 1,
      "ColorRange": 0,
      "ColorTransfer": 1,
      "Encoder": "nvenc_h265_10bit",
      "HardwareDecode": 0,
      "Level": "auto",
      "MultiPass": false,
      "Options": "multipass=fullres:rc-lookahead=32:bf=4:b_ref_mode=middle:spatial-aq=1:temporal-aq=1:aq-strength=8:maxrate=2500k:bufsize=5000k",
      "PasshtruHDRDynamicMetadata": 6,
      "Preset": "slowest",
      "Profile": "auto",
      "Quality": 26.0,
      "Tune": "",
      "Turbo": false
    }
  },
  "Title": {
    "AngleCount": 1,
    "AudioList": [
      {
        "Attributes": {
          "AltCommentary": false,
          "Commentary": false,
          "Default": true,
          "Normal": false,
          "Secondary": false,
          "VisuallyImpaired": false
        },
        "BitRate": 1,
        "ChannelCount": 2,
        "ChannelLayout": "stereo",
        "Codec": 65536,
        "CodecName": "aac",
        "CodecParam": 86018,
        "Description": "日本語 (AAC LC, 2.0 ch)",
        "LFECount": 0,
        "Language": "日本語",
        "LanguageCode": "jpn",
        "Name": "Stereo",
        "SampleRate": 48000,
        "TrackNumber": 1
      }
    ],
    "ChapterList": [
      {
        "Duration": {
          "Hours": 0,
          "Minutes": 22,
          "Seconds": 10,
          "Ticks": 119722140
        },
        "Name": "Chapter 1"
      },
      {
        "Duration": {
          "Hours": 0,
          "Minutes": 1,
          "Seconds": 28,
          "Ticks": 7897860
        },
        "Name": "Ending"
      },
      {
        "Duration": {
          "Hours": 0,
          "Minutes": 0,
          "Seconds": 11,
          "Ticks": 1021860
        },
        "Name": "Chapter 3"
      }
    ],
    "Color": {
      "BitDepth": 10,
      "ChromaLocation": 1,
      "ChromaSubsampling": "4:2:0",
      "Format": 62,
      "Matrix": 1,
      "Primary": 1,
      "Range": 1,
      "Transfer": 1
    },
    "Container": "matroska,webm",
    "Crop": [
      0,
      0,
      0,
      0
    ],
    "Duration": {
      "Hours": 0,
      "Minutes": 23,
      "Seconds": 49,
      "Ticks": 128641860
    },
    "FrameRate": {
      "Den": 1126125,
      "Num": 27000000
    },
    "Geometry": {
      "Height": 1080,
      "PAR": {
        "Den": 1,
        "Num": 1
      },
      "Width": 1920
    },
    "Index": 1,
    "InterlaceDetected": false,
    "KeepDuplicateTitles": false,
    "LooseCrop": [
      0,
      0,
      0,
      0
    ],
    "Metadata": {
      "CreationTime": "2026-01-01T00:00:00.000000Z",
      "Name": "Series S01E001 Episode Title"
    },
    "Name": "source",
    "Path": "/input/source.mkv",
    "Playlist": -1,
    "SubtitleList": [
      {
        "Attributes": {
          "4By3": false,
          "Children": false,
          "ClosedCaption": false,
          "Commentary": false,
          "Default": true,
          "Forced": true,
          "Large": false,
          "Letterbox": false,
          "Normal": false,
          "PanScan": false,
          "Wide": false
        },
        "Format": "text",
        "Language": "Francais (SSA)",
        "LanguageCode": "fra",
        "Source": 5,
        "SourceName": "SSA",
        "TrackNumber": 1
      }
    ],
    "Type": 3,
    "VideoCodec": "hevc"
  },
  "uiSettings": {
    "AlignAVStart": false,
    "AudioAllowAACPass": true,
    "AudioAllowAC3Pass": false,
    "AudioAllowALACPass": false,
    "AudioAllowDTSHDPass": false,
    "AudioAllowDTSPass": false,
    "AudioAllowEAC3Pass": false,
    "AudioAllowFLACPass": false,
    "AudioAllowMP2Pass": false,
    "AudioAllowMP3Pass": true,
    "AudioAllowOPUSPass": false,
    "AudioAllowPCMPass": false,
    "AudioAllowTRUEHDPass": false,
    "AudioAllowVORBISPass": false,
    "AudioAutomaticNamingBehavior": "unnamed",
    "AudioBitrate": "192",
    "AudioCopyMask": [
      "copy:mp3",
      "copy:aac"
    ],
    "AudioEncoder": "copy:ac3",
    "AudioEncoderFallback": "av_aac",
    "AudioLanguageList": [
      "jpn"
    ],
    "AudioList": [
      {
        "AudioBitrate": 160,
        "AudioCompressionLevel": -1.0,
        "AudioDitherMethod": "auto",
        "AudioEncoder": "copy",
        "AudioMixdown": "stereo",
        "AudioNormalizeMixLevel": false,
        "AudioSamplerate": "auto",
        "AudioTrackDRCSlider": 0.0,
        "AudioTrackGainSlider": 0.0,
        "AudioTrackQuality": 3.0,
        "AudioTrackQualityEnable": false
      }
    ],
    "AudioMixdown": "dpl2",
    "AudioSamplerate": "auto",
    "AudioSecondaryEncoderMode": true,
    "AudioTrack": "0",
    "AudioTrackDRCSlider": 0.9,
    "AudioTrackDRCValue": "Off",
    "AudioTrackGainSlider": 0.0,
    "AudioTrackName": "",
    "AudioTrackNamePassthru": true,
    "AudioTrackQuality": -1.0,
    "AudioTrackQualityEnable": false,
    "AudioTrackQualityValue": "-3",
    "AudioTrackQualityX": -3.0,
    "AudioTrackSelectionBehavior": "first",
    "ChapterMarkers": true,
    "ChildrenArray": [],
    "Default": false,
    "DisplayHeight": 1080,
    "FileFormat": "av_mkv",
    "Folder": false,
    "FolderOpen": false,
    "ImportFile": null,
    "ImportLanguage": "und",
    "ImportOffset": 0.0,
    "InlineParameterSets": false,
    "MainWhenComplete": "nothing",
    "MetaAlbumArtist": "",
    "MetaArtist": "",
    "MetaComment": "",
    "MetaCreationTime": "2026-10-25T10:06:04.000000Z",
    "MetaDescription": "",
    "MetaGenre": "",
    "MetaLongDescription": "",
    "MetaName": "Dusk Beyond the End of the World S01E01 La Dame aux étoiles",
    "MetaReleaseDate": "",
    "MetadataPassthru": true,
    "Mp4iPodCompatible": false,
    "Optimize": false,
    "PictureAllowUpscaling": false,
    "PictureAutoCrop": false,
    "PictureBottomCrop": 0,
    "PictureChromaSmoothCustom": "",
    "PictureChromaSmoothPreset": "off",
    "PictureChromaSmoothTune": "none",
    "PictureColorspaceCustom": "",
    "PictureColorspacePreset": "off",
    "PictureCombDetectCustom": "",
    "PictureCombDetectPreset": "off",
    "PictureCropMode": 2,
    "PictureDARWidth": 1920,
    "PictureDeblockCustom": "strength=strong:thresh=20:blocksize=8",
    "PictureDeblockPreset": "off",
    "PictureDeblockTune": "medium",
    "PictureDeinterlaceCustom": "",
    "PictureDeinterlaceDecomb": false,
    "PictureDeinterlaceFilter": "off",
    "PictureDeinterlacePreset": "",
    "PictureDenoiseCustom": "",
    "PictureDenoiseFilter": "off",
    "PictureDenoisePreset": "",
    "PictureDenoiseTune": "none",
    "PictureDetelecine": "off",
    "PictureDetelecineCustom": "",
    "PictureForceHeight": 0,
    "PictureForceWidth": 0,
    "PictureHeight": 1080.0,
    "PictureItuPAR": false,
    "PictureKeepRatio": true,
    "PictureLeftCrop": 0,
    "PictureModulus": 2,
    "PicturePAR": "auto",
    "PicturePARHeight": 1,
    "PicturePARWidth": 1,
    "PicturePadBottom": 0,
    "PicturePadColor": "black",
    "PicturePadLeft": 0,
    "PicturePadMode": "none",
    "PicturePadRight": 0,
    "PicturePadTop": 0,
    "PictureRightCrop": 0,
    "PictureRotate": "angle=0:hflip=0",
    "PictureSharpenCustom": "",
    "PictureSharpenFilter": "off",
    "PictureSharpenPreset": "",
    "PictureSharpenTune": "",
    "PictureTopCrop": 0,
    "PictureUseMaximumSize": true,
    "PictureWidth": 1920.0,
    "Preferences": {
      "ActivityFontFamily": "monospace",
      "ActivityFontSize": 8.0,
      "AddCC": false,
      "AutoScan": false,
      "CustomNotificationMessage": "",
      "CustomTmpDir": null,
      "CustomTmpEnable": false,
      "DiskFreeCheck": true,
      "DiskFreeLimitGB": 2.0,
      "EncodeLogLocation": false,
      "ExcludedFileExtensions": [
        "jpg",
        "png",
        "srt",
        "ssa",
        "ass",
        "txt"
      ],
      "ExportDirectory": "/output",
      "LimitMaxDuration": false,
      "LogLongevity": "immortal",
      "LoggingLevel": "1",
      "LowBatteryLevel": 15,
      "MaxTitleDuration": 0.0,
      "MinTitleDuration": 10.0,
      "NativeFileChooser": true,
      "NotifyOnEncodeDone": false,
      "NotifyOnQueueDone": true,
      "PauseEncodingOnBatteryPower": false,
      "PauseEncodingOnLowBattery": false,
      "PauseEncodingOnPowerSave": true,
      "PreferredLanguage": "und",
      "RecursiveFolderScan": false,
      "RemoveFinishedJobs": false,
      "ShowMiniPreview": true,
      "SrtDir": "/input",
      "SyncTitleSettings": true,
      "UiLanguage": "",
      "UseM4v": false,
      "VideoQualityGranularity": "1",
      "WhenComplete": "nothing",
      "allow_tweaks": false,
      "auto_name": true,
      "auto_name_template": "{source}",
      "default_source": "/input/source.mkv",
      "destination_dir": "/output",
      "hbfd": false,
      "hbfd_feature": false,
      "live_duration": 15.0,
      "presets_window_height": 600,
      "presets_window_width": 323,
      "preview_count": 10.0,
      "preview_show_crop": false,
      "preview_x": -1,
      "preview_y": -1,
      "reduce_hd_preview": true,
      "show_queue_sidebar": true,
      "use_dvdnav": true,
      "version": "0.1",
      "window_height": 715,
      "window_width": 1098
    },
    "PreferredLanguage": "fra",
    "PresetCategory": "new",
    "PresetDescription": "1080p H.265 10-bit NVENC for Anime (<500MB cap), Japanese Audio (AAC/MP3), French Subtitles.",
    "PresetDisabled": false,
    "PresetFullName": "/My Presets/Anime 1080p NVENC VOSTFR (<500MB)",
    "PresetName": "Anime 1080p NVENC VOSTFR (<500MB)",
    "PtoPType": "chapter",
    "QueueWhenComplete": "nothing",
    "SrtCodeset": "ISO-8859-1",
    "SubtitleAddCC": false,
    "SubtitleAddForeignAudioSearch": false,
    "SubtitleAddForeignAudioSubtitle": false,
    "SubtitleBurnBDSub": false,
    "SubtitleBurnBehavior": "none",
    "SubtitleBurnDVDSub": false,
    "SubtitleLanguageList": [
      "fra"
    ],
    "SubtitleTrack": "0",
    "SubtitleTrackNamePassthru": true,
    "SubtitleTrackSelectionBehavior": "all",
    "Type": 1,
    "UsesPictureFilters": false,
    "VideoAvgBitrate": 0.0,
    "VideoColorMatrixCodeOverride": 0,
    "VideoColorRange": "auto",
    "VideoEncoder": "nvenc_h265_10bit",
    "VideoFramerate": "auto",
    "VideoFramerateCFR": false,
    "VideoFramerateMode": "vfr",
    "VideoFrameratePFR": false,
    "VideoFramerateVFR": true,
    "VideoGrayScale": false,
    "VideoHWDecode": 0,
    "VideoLevel": "auto",
    "VideoMultiPass": false,
    "VideoOptionExtra": "multipass=fullres:rc-lookahead=32:bf=4:b_ref_mode=middle:spatial-aq=1:temporal-aq=1:aq-strength=8:maxrate=2500k:bufsize=5000k",
    "VideoPasshtruHDRDynamicMetadata": "all",
    "VideoPreset": "slowest",
    "VideoPresetSlider": 6,
    "VideoProfile": "auto",
    "VideoQualitySlider": 26,
    "VideoQualityType": 2,
    "VideoScaler": "swscale",
    "VideoTune": "",
    "VideoTurboMultiPass": false,
    "activity_location": "",
    "angle": 1,
    "angle_count": 1,
    "chapter_list": [],
    "crop_mode": "none",
    "dest_dir": "/output",
    "dest_file": "destination.h265.mkv",
    "destination": "/output/destination.h265.mkv",
    "end_point": 3,
    "final_aspect_ratio": "16:9",
    "final_storage_size": "1920 x 1080",
    "hflip": 0,
    "job_status": 0,
    "job_unique_id": 0,
    "preset_modified": false,
    "preset_reload": false,
    "preview_frame": 2,
    "resolution_limit": "1080p",
    "rotate": "0",
    "scale_height": 1080,
    "scale_width": 1920,
    "single_title": 1,
    "source": "/input/source.mkv",
    "source_aspect_ratio": "16:9",
    "source_display_size": "1920 x 1080",
    "source_height": 1080,
    "source_label": "source",
    "source_storage_size": "1920 x 1080",
    "source_width": 1920,
    "start_frame": -1,
    "start_point": 1,
    "title": 1,
    "title_selected": true,
    "volume": "source",
    "vquality_type_bitrate": false,
    "vquality_type_constant": true,
    "x264FastDecode": false,
    "x264Option": "",
    "x264UseAdvancedOptions": false,
    "x264ZeroLatency": false
  }
};
