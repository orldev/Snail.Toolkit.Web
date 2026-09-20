FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    SHELL=/bin/bash \
    MOZBUILD_STATE_PATH=/build/.mozbuild

RUN apt-get update && apt-get install -y --no-install-recommends \
      git curl wget ca-certificates python3 python3-pip python3-venv \
      build-essential clang lld llvm pkg-config m4 unzip zip file \
      libpython3-dev mercurial nasm \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /build

# Ревизия из browser_patches/firefox/UPSTREAM_CONFIG.sh — ровно та, от которой
# Playwright ведёт свои патчи. Менять её вместе с patches/ и не по отдельности.
#
# И БРАТЬ ЕЁ У ТЕГА, ЧЕЙ КЛИЕНТ МЫ ВЕЗЁМ, А НЕ У main. Ревизия и снимок patches/
# ниже — с тега v1.62.0, потому что для .NET последний Microsoft.Playwright —
# 1.62.0, и другого клиента у нас нет.
#
# ЧЕМ ЭТО ИЗМЕРЕНО (20.09.2026, PATCHES.md раздел 7): прежний снимок был снят с
# main и давал Firefox 155.0, чей juggler говорит на протоколе эпохи 1.63. С
# драйвером 1.63 наша сборка читает тяжёлую страницу (714 959 знаков), с
# драйвером 1.62 — умирает вместе с сервером. Рукопожатие расхождения не ловит:
# 428 сравнивает версии КЛИЕНТА И СЕРВЕРА, а не клиента и juggler в браузере.
ARG FIREFOX_REV=f1b6c0f86b96b7e0688c26f65803576f27cdaf88

RUN git init firefox \
 && cd firefox \
 && git remote add origin https://github.com/mozilla-firefox/firefox.git \
 && git -c protocol.version=2 fetch --depth 1 origin ${FIREFOX_REV} \
 && git checkout FETCH_HEAD

COPY patches/ /build/patches/

# 1. juggler — реализация протокола Playwright, ложится в корень дерева.
# 2. bootstrap.diff — 74 файла, ими Firefox становится управляемым браузером.
# 3. Наша единственная правка. Playwright заменяет в Navigator::Webdriver()
#    `return false` на `return true`; возвращаем обратно, чтобы navigator.webdriver
#    был ложью по факту сборки, а не по подмене из скрипта. Подмену через
#    Object.defineProperty видно по дескриптору и по toString геттера —
#    скомпилированную ложь проверять нечем.
RUN cd firefox \
 && cp -r /build/patches/juggler ./juggler \
 && git apply --3way /build/patches/patches/bootstrap.diff \
 && python3 - <<'PY'
import pathlib

path = pathlib.Path('/build/firefox/dom/base/Navigator.cpp')
text = path.read_text()

before = "  // Playwright is automating the browser, so we should pretend to be a webdriver\n  return true;\n}"
after = "  // Reverted: this build does not announce itself as a webdriver.\n  return false;\n}"

assert text.count(before) == 1, f"ожидал ровно одно вхождение, нашёл {text.count(before)}"

path.write_text(text.replace(before, after))
print("OK: Navigator::Webdriver() -> false")
PY

# Сколько заданий на сборку. Правило: не больше, чем ГБ памяти делить на два —
# линковка libxul самое прожорливое место. 32 ГБ -> 8..12, 16 ГБ -> 4..6.
ARG JOBS=8

# --host и --target названы явно, и это не украшение. Configure выводит тройку
# как x86_64-pc-linux-gnu, а затем ищет для неё цель rustc; нынешний rustc
# (1.98.1) отдаёт рядом x86_64-unknown-linux-gnu вместе с gnuasan, gnumsan,
# gnutsan и gnux32, ни один из отборов в rust.configure не остаётся с единственным
# кандидатом, и сборка падает на «Don't know how to translate x86_64-pc-linux-gnu
# for rustc» — через восемь секунд после старта, замерено 20.09.2026 на дереве
# тега v1.62.0. Названная тройка совпадает с целью rustc дословно, отбор находит
# её сразу, и configure проходит.
RUN cd firefox && printf '%s\n' \
  'ac_add_options --host=x86_64-unknown-linux-gnu' \
  'ac_add_options --target=x86_64-unknown-linux-gnu' \
  'ac_add_options --enable-application=browser' \
  'ac_add_options --enable-optimize' \
  'ac_add_options --disable-debug' \
  'ac_add_options --disable-debug-symbols' \
  'ac_add_options --disable-tests' \
  'ac_add_options --enable-linker=lld' \
  'ac_add_options --disable-crashreporter' \
  'ac_add_options --disable-updater' \
  'mk_add_options MOZ_OBJDIR=/build/objdir' \
  "mk_add_options MOZ_MAKE_FLAGS=\"-j${JOBS}\"" \
  > .mozconfig && cat .mozconfig

RUN cd firefox && ./mach --no-interactive bootstrap --application-choice browser

# Сборка вынесена из образа в запуск: падение линковки не должно заставлять
# заново качать дерево и тулчейн, они уже лежат в слоях.
CMD ["/bin/bash", "-lc", "cd /build/firefox && ./mach build && ./mach package && mkdir -p /out && cp -v /build/objdir/dist/firefox-*.tar.* /out/ && ls -la /out"]
