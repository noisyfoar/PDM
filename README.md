# PDM

Мини-PDM: импорт CAD-документов, версии и состояния объектов, дерево состава,
масса сборки и сводная спецификация. Windows, .NET 10, WPF, PostgreSQL, EF Core, Jab, xUnit.

## Запуск

Создайте базу PostgreSQL и настройте `ConnectionStrings.Pdm` в
`Shell/appsettings.json` (по умолчанию порт 55432, база `pdm`). Пользователю БД
нужны права изменять схему. Миграции из `src/Infrastructure/Migrations`
применяются при запуске автоматически.

Из корня репозитория:

```powershell
dotnet build
dotnet test
dotnet run --project Shell/Shell.csproj
```
## Тесты

Обычные тесты не требуют БД или Docker. Интеграционные проверки запускаются
отдельно; нужна тестовая БД с правами создания и удаления схем:

```powershell
$env:PDM_TEST_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=pdm_test;Username=admin;Password=admin'
$env:PDM_CAD_EXPORT_ROOT = (Resolve-Path Data/cad-export).Path
dotnet test tests/Integration.Tests/Integration.Tests.csproj
```

Каждый интеграционный тест создаёт и удаляет собственную схему.
Для расчёта в UI: открыть карточку сборки → «Масса и спецификация» → «Рассчитать».

## Функциональность 

Поиск загружает все объекты в память; для больших каталогов нужны запросы
с фильтрацией и пагинацией. Дополнительные пункты ТЗ — экспорт, второй провайдер
БД, сравнение версий, логирование и отмена импорта из UI — не реализованы.
