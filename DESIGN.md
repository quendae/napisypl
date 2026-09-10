# NapisyPL UI Design

## Produkt i zadanie

NapisyPL jest małym narzędziem desktopowym, a nie panelem administracyjnym. Pierwszy ekran ma prowadzić użytkownika przez jeden przepływ: wybierz film/napisy → wybierz tłumacza → tłumacz → otwórz wynik.

## Kierunek wizualny

Interfejs ma przypominać precyzyjne narzędzie do napisów, nie „AI dashboard”. Bez gradientów, szklanych kart, zbędnych ikon i bocznej nawigacji. Podstawą są jasne powierzchnie, mocna typografia systemowa i jeden motyw zaczerpnięty z napisów filmowych: ciemny pasek cue z żółtym znacznikiem `EN → PL`.

### Tokeny

- Canvas: `#F4F5F7`
- Surface: `#FFFFFF`
- Ink: `#161A22`
- Muted: `#667085`
- Border: `#D8DDE5`
- Subtitle yellow: `#F2C94C` (akcent dekoracyjny, nie drobny tekst)
- Action: `#202A3A`
- Success: `#18794E`
- Error: `#B42318`

Typography: Segoe UI na Windows; normalny tekst 14–16 px, nagłówek 26 px, utility/timestamps 12–13 px. Brak fontów dołączanych do projektu.

## Układ

Okno ~820×720. Na górze mały „cue strip” z `00:00:00,000  EN → PL`, potem nazwa i jedno zdanie opisu. Największym elementem jest pole drop-zone. Konfiguracja tłumacza jest niżej i ma znaczenie drugorzędne. Przycisk `Tłumacz na polski` jest jedyną mocną akcją. Status, progress i otwarcie folderu pozostają w stabilnym dolnym obszarze, aby layout nie skakał podczas pracy.

## Zachowanie

- Drop-zone ma wyraźny przycisk wyboru pliku i obsługuje przeciąganie plików.
- Klucz API jest maskowany i nigdy nie jest zapisywany na dysku.
- Pola model/Base URL zmieniają sens zależnie od providera, ale pozostają w stałym miejscu.
- Podczas pracy główna akcja jest disabled, a `Anuluj` aktywny.
- Błędy pojawiają się tekstowo w obszarze statusu; nie używamy modalnych alertów.
- Bitmapowe ścieżki pozostają widoczne i są jawnie opisane jako niewspierane.
- Układ jest przewijalny przy małym oknie i używalny klawiaturą.
