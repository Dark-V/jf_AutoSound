# Configuration

The settings page and `/SidecarBridge/Libraries`, `/SidecarBridge/Preview`, `/SidecarBridge/Report` require Jellyfin's `RequiresElevation` policy. Jellyfin's standard plugin-configuration API controls saving. File names are rendered with DOM `textContent`; log fields have control characters removed.

## Built-in behavior

No libraries are enabled initially. Audio and subtitles have separate switches. The video directory is the default search scope. When its name is `Video`, the scope is its parent, allowing sibling audio/subtitle directories.

To process just one series, enable its library and enter its absolute server path in **Only this folder**. The scan and preview stay inside that folder and its subfolders, even if additional parent levels are configured. Leave the field empty to process all folders in the enabled libraries.

Nested folders containing `Audio`, `Sound`, `Sub`, `Subs`, `Subtitles`, `озвучка` or `субтитры` are recognized. Examples: `Rus Sound/Example Voice`, `English Audio/Team`, `RUS Sound [Studio]`, `Rus Subs [Group]`. A generic folder name is not a language.

The longest complete video basename takes precedence. For `Series S01E01.mkv` and `Series S01E01.ru.Alternate voice.mka`, the plugin uses `ru` as the language and `Alternate voice` as the track title. A label can differ from the folder name. If a suffix omits the title or language, the plugin uses the leaf folder (or its final `[Group]` part) for the title and a recognized language prefix for the language. If no language is explicit, it stays undefined.

Only MKV/MP4 videos, MKA audio and ASS/SSA/SRT/VTT subtitles are included in this version. Other files are ignored. The plugin does not edit embedded stream metadata.

## Search and readiness limits

| Setting | Default | Meaning |
| --- | --- | --- |
| `MaximumDepth` | 4 | Maximum directory nesting below the scope; hard cap 12 |
| `MaximumEntries` | 20000 | Total entries inspected; hard cap 200000; exceeding the limit aborts that scope before writes |
| `MinimumAgeSeconds` | 30 | Recent/nonzero source guard; minimum 1 second |
| `ScopeParentLevels` | 0 | Additional parent levels, capped at 3 and the enabled library root |
| `RemoveStaleOwnedLinks` | false | Remove only identity-verified plugin-owned links with confirmed missing sources |

Keep scopes narrow. A scope does not borrow episode numbers from the rest of the library. File readiness uses age, identity, length, readable handles and before/after metadata checks. A paused writer with old timestamps cannot be reliably identified on every filesystem; see [limitations](limitations.md).

## Optional directory rules

The advanced JSON editor accepts a `DirectoryRules` array. A pattern sees the source directory **relative to the scope**, with `/` separators. The first matching rule wins. A 100 ms timeout applies to each regex operation.

```json
[
  {
    "Pattern": "^Dubs/French(?:/|$)",
    "Language": "fra",
    "Title": "French Team",
    "Default": false,
    "Forced": false,
    "HearingImpaired": false
  },
  {
    "Pattern": "^Captions/Signs$",
    "Language": "jpn",
    "Title": "Signs",
    "Forced": true
  }
]
```

`MatchPattern` and `MatchReplacement` optionally transform a sidecar's comparison stem, after recognized filename suffixes are removed. They never rename source files. For example, `"MatchPattern": "^Dub - "`, `"MatchReplacement": ""`. A literal/normalized full basename match still has priority. Timeout or invalid regex errors produce skipped entries.

## Explicit mappings

Use this when names differ or several editions exist. Both paths are relative to the current scope:

```json
[
  {
    "Source": "Audio/Studio/Custom track.mka",
    "Video": "Video/Film - Director Cut.mkv"
  }
]
```

Mappings cannot escape the scope or library root, traverse symlinks/junctions, or refer to an undiscovered video. Several mappings for one source are rejected. If several sources produce the same destination, all colliding plans are skipped; assign distinct titles.

## Titles, languages and flags

The name format is:

```text
VideoBase.Studio.language.default.forced.sdh.ext
```

Only populated/explicit fields are included. An unlabelled track uses `External`; `Signs` alone never means forced. Filename language takes precedence over a directory rule, which takes precedence over an explicit directory language prefix. `und` means leave language undefined.

The plugin reads explicit terminal filename tokens `default`, `forced`, `foreign`, `sdh`, `cc` and `hi`, following the Hindi/hearing-impaired distinction. For example, `Film.hi.srt` means Hindi; `Film.en.hi.srt` means English hearing-impaired. Rules can explicitly enable flags. Embedded metadata is left to Jellyfin.

Jellyfin 12.4's [`ExternalPathParser`](https://github.com/jellyfin/jellyfin/blob/v12.4/Emby.Naming/ExternalFiles/ExternalPathParser.cs) recognizes `default`, `forced` and `foreign` by **substring**. Consequently a studio named `Default Studio` cannot safely be emitted literally. Sidecar Bridge reversibly percent-encodes one letter: `%44efault Studio`. Dots and unsafe filename characters are escaped too. A title that is itself a language code/name or hearing-impaired token gets a `Studio ` prefix. Ordinary `Example Voice`, `SubsGroup` and `Signs` remain unchanged. This avoids accidental flags/languages, at the cost of a visible escape for reserved names.

MKA with several streams is different: Jellyfin preserves per-stream embedded metadata and ignores filename flags for multi-stream containers. A filename does not rename each stream, and an embedded title/language can take precedence over the filename. Sidecar Bridge does not rewrite tags.

## Preview and reports

Select a library and enter a complete server-side video path. Preview uses the current form values, reads files and computes actions, without creating links or modifying the ownership registry. It can preview a library before enabling it.

Statuses: `Planned`, `Created`, `Existing`, `Replaced`, `PlannedReplacement`, `Conflict`, `Deferred`, `Skipped`, `Removed` and `PlannedRemoval`. Reasons explain mismatches, ambiguous editions, permissions, limits and native hardlink errors. The recent report retains the last 1000 entries in memory and resets on server restart; persistent ownership/retry data has a separate purpose.
