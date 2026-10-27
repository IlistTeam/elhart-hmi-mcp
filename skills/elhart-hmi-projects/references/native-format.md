# Нативный формат и причины прежних ошибок

Проверено на ELHART HMI Soft 3.0.15569.0. Это наблюдения конкретной версии,
а не универсальный стандарт. Для другой версии используй её прототипы и каталог.

## Профиль, адреса и файлы

- XML-модель: `http://schemas.flexem.com/fs/model/1`; xsi:
  `http://www.w3.org/2001/XMLSchema-instance`.
- `.fsprj` — входной файл; основное содержимое в HMI. Одного `.fsprj` недостаточно.
  Окна: `HMI/Frame/*/*.wcfg`, библиотеки: `HMI/Lib/Graphics/*.fsvg`,
  макросы: `HMI/Macro/*.cfg`.
- Каталог `Device/HMI/HMI_index.CFG`, OEM `Device/HMI/Custom/119.xml`:
  ID2050/F010/ECP10/1024×768/runtime a8;
  ID2005/F007/ECP07/1024×600/runtime a8. При смене модели проверь
  `HMI/System/ConnectInfo.cfg` и геометрию. Его корень бывает HMIProjectInfo
  или ConnectInfo; сериализатору передай фактические имя/namespace.
- В DTO внутренний LB: Raw.RegisterId=254, LW=65791. Каталожное 255 не является
  готовым Raw LW. Сохраняй полный адрес, GUID устройства, длину и метаданные.
  GUID локального устройства конкретен для проекта.
- DTO: `Flexem.Studio.HMI.Window.HMIWindowInfoDto`,
  `Flexem.Studio.HMI.GraphInfoDto`, `Flexem.Studio.Macro.MacroInfoDto`
  в `Bin/Flexem.Studio.Dtos.dll`. Используй Windows PowerShell5.1/.NET Framework;
  Core PowerShell может быть несовместим.

## XML: префиксы типов имеют область действия

Файл может объявлять `xmlns:a` несколько раз: у Constant это XMLSchema,
у коллекции Microsoft Arrays. `i:type="a:double"` содержит QName как строку;
ElementTree не сохраняет его семантику автоматически. Словарь всех start-ns
с последним значением каждого префикса разрушает ранние объявления, создавая
`File damage` даже у синтаксически верного XML.

Используй minidom через native_xml.py или штатный DataContractSerializer.
При переносе поддерева между документами перенеси его xmlns. Не удаляй
unused-looking объявления: они могут использоваться только внутри i:type.
Сохраняй порядок членов DTO из прототипа. Не вставляй недостающие поля по
алфавиту. i:nil, пустой узел и отсутствующий узел могут различаться по смыслу.

В `ControlSettingsPropertyInfoDto` версии 3.0.15569 поле
`IsDisableWhenNotMatch` имеет `DataMember.Order=2`: оно идёт **после**
группы Order=1, включая `Version`, и **перед** группами Order=3…5.
`EnableType`, `IsEnabled`, `VisibilityInfo`, `IsTriggerMacroAfter` относятся
к группе без заданного Order и идут раньше. Общая алфавитная сортировка
смешивает группы и повреждает загрузку: после Order=2 загрузчик пропускает
поля меньшего порядка без исключения. Поля базового DTO сериализуются раньше
производного; внутри одной группы порядок задаётся сериализуемыми именами,
включая `DataMember.Name`, а не только именами свойств C#.

Предпочитай штатный DataContractSerializer или полный сохранённый прототип.
При ремонте XML получи порядок из метаданных установленной DLL, сохрани
поддеревья и QName, затем проверь реальные значения десериализованного DTO.
Не пересохраняй неисправный DTO до восстановления XML: пропущенные данные
уже стали null/false и могут окончательно потеряться.

## Кнопки и индикаторы

| Ошибка | Проверенное решение |
| --- | --- |
| The number of switch entries cannot be empty | UseSwitch=true требует непустой ActionList. Для декорации отключай переключение. |
| ArgumentNullException dto в InvalidInput/IndicatorWord | Сохраняй полные Usage/RegisterControl/Word/InvalidInput; в применимом случае Actions=KeepCurrentState. |
| Ошибка состояния/подписи | Для N словных условий нужны N обычных состояний плюс error/fallback в тегах и оформлении. TagPropertyInfoBuildAdapter запрашивает индекс N. |
| Кнопка исчезает после нажатия | Полная пара bit-состояний 0/1 и соответствующие Tag.Statuses/GraphStateInfos; в проверенном прототипе IsNegativeLogic=false/true. Не путать разрешение управления и Visibility. |
| ChildDataPortal.Fetch failed | Проверять HMIWindowAction.FetchAsChild и TimerListInfo.FetchAsChild; сохранять у действия и неиспользуемые BitAddress/WordAddress DTO. |

UseIndicator и UseSwitch независимы. Не удаляй действия или состояние, чтобы
замаскировать ошибку. Новые ID требуют обновления ссылок окон, переходов,
макросов, библиотечного индекса. В существующем проекте сохраняй ID.
`Thumbnails/ThumbnailConfigs.xml` — Dictionary<Guid,string>; обновляй .fsvg,
индекс и `Thumbnails/<id>/Status0.png` согласованно.

## Геометрия, круги, значки и текст

- Размер компонента, GraphProperty.GraphWidth/GraphHeight, библиотека,
  геометрия каждого состояния и фактический PNG должны соответствовать.
  Для разных размеров общей библиотеки нужны отдельные варианты.
- У чистых кругов все подписи пустые, **включая error/fallback**;
  Tag.IsDecisionTagOverflow=false. «Нет данных» расширяло квадрат до 156×44,
  превращая круг в овал даже при правильных размерах основного состояния.
- Круг: квадрат компонента/PNG, NormalInfo.SizeZoom.IsFixLatitudeScale=true,
  IsUseOriginalSize=true; PNG alpha включена.
- В проверенном сборщике смесь RectangleInfo+StaticPictureInfo теряла значки.
  Для такой декорации надёжен один PNG на состояние. Текст/действия нативные.
  Это workaround версии, а не требование делать весь экран картинкой.
- Значок и текст: нужен **корневой** Tag.HorizontalAlignment=Left и Margin.Left,
  согласованные language margins. Center игнорировал резерв под значок;
  один language margin не помогал independentTag. В SLM значок x18…48,
  подпись с 60; у бейджа отступ 40. Для другого размера вычисляй отступ заново.
- Числа рядом с единицей: Numeric.FontData.FontPostion HorizontalAlignmentType=Right,
  HorizontalMargin=0; StaticText TagPosition и ContentPosition=Left.
  Единица сразу за правым краем поля цифр плюс зазор (в SLM8 px).
  Проверь отрицательные/максимальные/дробные значения и переполнение текста.

## Числовые и строковые поля

NullReferenceException сборщика числового поля устранялась полными NumericLimit
в NormalData и NumericSetingData: MinLimit/MaxLimit, IsConstant, Constant,
Variable/Address metadata; UseLimit=true у проверенного прототипа.
Сохраняй тип, пределы и IsShowZero; нули отображай, если не требуется иное.

Выделяй достаточно слов для строки и NUL: MM:SS.d =7+1 байт =4 LW.
Согласуй CharCount, ReadAddress.Raw.Length, AddressLength/WordCount, длину
записи, соседний регистр и геометрию поля.
