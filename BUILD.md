# Сборка VMGenerator в один EXE файл

## Команда для сборки

Выполните следующую команду в корневой папке проекта:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Результат

После сборки вы найдете готовый **VMGenerator.exe** по пути:

```
bin\Release\net8.0-windows\win-x64\publish\VMGenerator.exe
```

## Как это работает

1. **Первый запуск**:
   - Приложение создаст файл `.vmgenerator` рядом с exe
   - В этот файл будут записаны настройки по умолчанию из встроенного `config.json`

2. **Последующие запуски**:
   - Приложение будет читать настройки из `.vmgenerator`
   - При изменении настроек через Config окно, они сохраняются в `.vmgenerator`

3. **Перемещение приложения**:
   - Можно перемещать `VMGenerator.exe` в любую папку
   - Файл `.vmgenerator` с настройками всегда будет рядом с exe
   - Если удалить `.vmgenerator`, он будет создан заново с дефолтными настройками

## Особенности

- ✅ Один самодостаточный EXE файл (~200-300 МБ)
- ✅ Не требует установки .NET Runtime
- ✅ Настройки хранятся в `.vmgenerator` рядом с exe
- ✅ config.json встроен внутрь exe как ресурс
- ✅ Поддержка Windows 10/11 x64

## Дополнительно

Если хотите уменьшить размер файла, можете использовать:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Это создаст EXE ~5-10 МБ, но потребует установленного .NET 8 Runtime на целевой машине.
