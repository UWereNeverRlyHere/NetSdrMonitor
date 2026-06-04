namespace NetSdrMonitor.Desktop.Behaviors;

/// <summary>
/// Рядок таблиці, що вміє разово анімувати свою появу. Прапорець ставить джерело даних
/// (для нових записів), а гасить його поведінка <see cref="RowAppearAnimation"/> після
/// першого показу — тож ефект не повторюється при прокручуванні чи переробці контейнерів.
/// </summary>
public interface IAnimatedRow
{
    bool NeedFadeIn { get; set; }
}
