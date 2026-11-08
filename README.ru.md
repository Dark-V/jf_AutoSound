<img src="docs/assets/icon.png" width="100" alt="Иконка Sidecar Bridge">

# jf_AutoSound · Sidecar Bridge

**Плагин Jellyfin, который находит озвучки и субтитры в подпапках раздачи и создаёт рядом с видео hardlinks с понятными Jellyfin именами.** Добавили файлы → обычное сканирование библиотеки → дорожки появились в проигрывателе. Оригиналы остаются на месте. Копирования медиа, remux и перекодирования нет.

[English](README.md) · [Установка](docs/installation.md) · [Настройки](docs/configuration.md) · [Что проверено](docs/verification.md)

![Было и стало: дорожки из подпапок становятся видны Jellyfin](docs/assets/before-after.png)

## Было

```text
Youjo Senki/
├── Youjo Senki TV 01 серия.mkv
├── Youjo Senki TV 02 серия.mkv
├── Youjo Senki TV Sp 00 серия.mkv
├── Youjo Senki TV Sp 01 серия.mkv
├── Rus Sound/
│   └── NDA Studio/
│       ├── Youjo Senki TV 01 серия.mka
│       └── Youjo Senki TV Sp 01 серия.mka
└── Rus Subs/
    └── SubsGroup/
        └── Youjo Senki TV 01 серия.ass
```

## Что добавится рядом с видео

```text
Youjo Senki TV 01 серия.NDA Studio.rus.mka
Youjo Senki TV 01 серия.SubsGroup.rus.ass
Youjo Senki TV Sp 01 серия.NDA Studio.rus.mka
```

Это дополнительные имена существующих файлов. Второй копии озвучки или субтитров на диске нет. В Jellyfin появятся внешние дорожки с языком и названием студии. Итоговая подпись зависит также от встроенных тегов и клиента.

Sidecar Bridge сопоставляет дорожки `TV 01` и `Sp 01` с разными файлами. Номер `Sp 00` допустим. Если в папке озвучек есть только обычные серии, у спецвыпусков будут лишь их собственные дорожки и субтитры.

Номера сезонов и группировку версий задаёт Jellyfin. Для отдельных карточек спецвыпусков используйте сезон `00` и имена `S00E…` либо локальные метаданные с номером сезона `0`. Если Jellyfin уже связал два видео как версии, разделите их через меню эпизода, затем обновите метаданные спецвыпусков. [Правила именования Jellyfin](https://jellyfin.org/docs/general/server/media/shows/)

## Ещё один обычный случай

```text
Film/
├── Video/Film.mkv
├── English Audio/Studio/Film.mka
└── Subtitles/Group/Film.srt
```

Рядом с `Video/Film.mkv` появятся `Film.Studio.eng.mka` и `Film.Group.srt`. У субтитров язык останется неопределённым: папка `Subtitles` его не задаёт. Можно указать язык в настройках. Русский язык всем файлам автоматически не назначается.

![Два имени — один файл с общими данными](docs/assets/one-file.png)

## Как пользоваться

Поддерживаемая версия сервера **Jellyfin 12.4.0** (ABI `12.4.0.0`, .NET 10).

1. Откройте **Панель управления → Плагины → Репозитории** и добавьте адрес.

   ```text
   https://raw.githubusercontent.com/Dark-V/jf_AutoSound/refs/heads/main/manifest.json
   ```

2. Откройте **Каталог**, выберите **Sidecar Bridge** и установите последнюю совместимую версию.
3. Перезапустите Jellyfin. Откройте настройки **Sidecar Bridge**, включите нужные библиотеки и сохраните настройки.
4. Проверьте путь к видео кнопкой **Preview** и запустите обычное сканирование библиотеки.

Задача **Обновить плагины** скачивает новые совместимые версии. Для активации скачанного обновления перезапустите сервер. Если у вас вручную установлена сборка `0.1.0.0`, один раз установите новую версию из каталога. Настройки и учёт созданных ссылок сохраняются. [Установка и обновление](docs/installation.md)

Изначально библиотеки выключены. Поддерживаются MKV/MP4, MKA, ASS/SSA/SRT/VTT. Встроенные правила понимают `Audio`, `Sound`, `Subs`, `Subtitles`, языковые префиксы и названия студий в скобках. Для нестандартных структур есть правила каталогов и явные соответствия.

Недавно записанные файлы по умолчанию откладываются до следующего обычного прохода: минимальный возраст — 30 секунд. Источник и ссылка должны находиться на одной файловой системе. Jellyfin нужны права чтения исходников и записи в каталог видео. Если hardlink создать нельзя, причина будет в отчёте; копирование или symlink вместо него не используются.

Плагин работает внутри Jellyfin. Отдельные скрипты, контейнеры, cron и привязка к qBittorrent не нужны. Повторное сканирование после создания ссылок не запускается: используется штатный анализатор Jellyfin в текущем обновлении.

## Проверки и сборка

На Windows x64, Linux x64 и Linux ARM64 прошли проверки ядра и установка ZIP в настоящий Jellyfin: дорожки в `MediaStreams` за один scan, новая озвучка у старого видео, замена исходника, повторный проход, флаги, MKA с несколькими потоками и запрет анонимного доступа к диагностике. Отдельно проверены права файловой системы и отказ hardlink между файловыми системами на Linux. Результаты — в [отчёте](docs/verification.md) и GitHub Actions. Docker и сетевые хранилища на рабочем сервере не проверялись.

```sh
dotnet restore src/Jellyfin.Plugin.SidecarBridge --locked-mode
dotnet restore tests/SidecarBridge.Tests --locked-mode
dotnet build src/Jellyfin.Plugin.SidecarBridge -c Release --no-restore
dotnet test tests/SidecarBridge.Tests -c Release --no-restore
python tools/package.py
```

GitHub Actions собирает [релизный ZIP](https://github.com/Dark-V/jf_AutoSound/releases) после прохождения проверок на Windows и Linux. Workflow проверяет публичный архив, вычисляет MD5 и добавляет версию в каталог. [Процесс публикации](CONTRIBUTING.md#releases)

Для локальной сборки нужны SDK из `global.json` и Python 3. ZIP сохраняется в `artifacts/`.

Подробности: [Linux, Docker, Windows](docs/installation.md), [языки и флаги](docs/configuration.md), [интеграция в сканирование](docs/architecture.md), [ограничения](docs/limitations.md). Лицензия MIT. Иконка и две схемы входят в исходники как SVG и PNG.
