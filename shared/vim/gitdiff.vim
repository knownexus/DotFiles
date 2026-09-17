" ===========================================================================
" Side-by-side git diff using vim's built-in diff mode — no plugin.
"
" The revision's copy of the current file is loaded into a scratch buffer in a
" vertical split, and both windows are put in diff mode: real syntax
" highlighting on both sides, changed regions highlighted within the line, and
" do / dp to move hunks between them.
"
"   :GitDiff              current file against HEAD
"   :GitDiff <rev>        against any revision (a sha, tag, branch, HEAD~3)
"   <space>d              toggle the diff for the current file
"
" In diff mode: ]c / [c jump hunks, do pulls the other side's hunk in,
" dp pushes yours out, zo opens a fold, :diffupdate refreshes.
" ===========================================================================

if exists('g:loaded_gitdiff')
    finish
endif
let g:loaded_gitdiff = 1

function! s:Close() abort
    " Wipe the scratch side and take every window out of diff mode.
    for l:win in range(1, winnr('$'))
        if getbufvar(winbufnr(l:win), 'gitdiff_scratch', 0)
            execute l:win . 'wincmd c'
            break
        endif
    endfor
    diffoff!
    normal! zR
endfunction

function! s:Open(rev) abort
    if &buftype !=# '' || expand('%') ==# ''
        echohl WarningMsg | echomsg 'GitDiff: not a file buffer' | echohl None
        return
    endif

    let l:rev  = empty(a:rev) ? 'HEAD' : a:rev
    let l:dir  = expand('%:p:h')
    let l:name = expand('%:t')
    let l:ft   = &filetype

    let l:lines = systemlist(
        \ 'git -C ' . shellescape(l:dir) . ' show ' . shellescape(l:rev . ':./' . l:name))
    if v:shell_error
        echohl WarningMsg
        echomsg 'GitDiff: ' . (empty(l:lines) ? 'not in a git repo' : l:lines[0])
        echohl None
        return
    endif

    diffthis
    " leftabove so the older revision sits on the left, as diffs read
    vertical leftabove new
    setlocal buftype=nofile bufhidden=wipe noswapfile nobuflisted nomodified
    let b:gitdiff_scratch = 1
    call setline(1, l:lines)
    let &l:filetype = l:ft
    setlocal nomodifiable
    execute 'silent file ' . fnameescape(l:name . ' @ ' . l:rev)
    diffthis
    setlocal nocursorline
    " Back to the working copy — that is the side you edit.
    wincmd p
    setlocal nocursorline
endfunction

function! s:Toggle(rev) abort
    if &diff
        call s:Close()
    else
        call s:Open(a:rev)
    endif
endfunction

command! -nargs=? -complete=customlist,s:Revs GitDiff call s:Toggle(<q-args>)
command! -bar GitDiffOff call s:Close()

function! s:Revs(lead, ...) abort
    let l:revs = ['HEAD', 'HEAD~1', 'HEAD~2', 'HEAD~3']
    let l:branches = systemlist('git branch --format=%(refname:short) 2>/dev/null')
    if !v:shell_error
        let l:revs += l:branches
    endif
    return filter(l:revs, 'v:val =~ "^" . a:lead')
endfunction

" --- Colours ---------------------------------------------------------------
" Vim's stock diff colours are close to unreadable: DiffChange is light
" magenta, DiffText bright red, DiffAdd light blue — saturated backgrounds
" that fight the syntax highlighting on top of them and glare on a dark
" terminal. These replace them with low-saturation backgrounds that leave the
" foreground text alone, so code still reads as code:
"
"   DiffAdd     a line only on this side          muted green
"   DiffDelete  filler where the other side has a line, dimmed almost away
"   DiffChange  a line that differs               neutral dark grey
"   DiffText    the part of it that actually changed, amber, the only
"               attention-grabbing colour on screen
function! s:DiffColours() abort
    highlight DiffAdd    cterm=NONE gui=NONE ctermfg=NONE guifg=NONE ctermbg=22  guibg=#1e3a24
    highlight DiffChange cterm=NONE gui=NONE ctermfg=NONE guifg=NONE ctermbg=237 guibg=#2b2f36
    highlight DiffText   cterm=bold gui=bold ctermfg=NONE guifg=NONE ctermbg=94  guibg=#6b4c15
    " Filler rows carry no content — make them near-invisible rather than a
    " wall of dashes in bright red.
    highlight DiffDelete cterm=NONE gui=NONE ctermfg=236  guifg=#2a2020 ctermbg=235 guibg=#241c1c
endfunction

" Re-apply after any :colorscheme, which resets highlight groups.
augroup gitdiff_colours
    autocmd!
    autocmd ColorScheme * call s:DiffColours()
    " Diff mode already colours whole lines — cursorline on top of that is
    " pure redraw cost for no readability gain, and diffs are where the lag
    " shows up most. This covers vimdiff launched by git difftool too.
    autocmd VimEnter,WinEnter * if &diff | setlocal nocursorline | endif
augroup END
call s:DiffColours()

" --- Performance in large diffs --------------------------------------------
" Measured on a 1500-line C# file, 50 scroll+redraws in a two-pane diff:
"
"     as-is                    265 ms
"     syntax off in both        11 ms      <- 24x faster
"     nocursorline             255 ms
"     synmaxcol=120            234 ms
"     scrollbind off           258 ms
"     regexpengine=1          2861 ms      <- far worse, do not use it
"
" A diff repaints BOTH panes on every scroll, and the cost is emitting the
" colour — every attribute change is an escape sequence, and with
" termguicolors they are long truecolor ones — not matching the syntax
" patterns (:syntime puts those at about 1ms per redraw). In a diff you are
" reading the diff colours; the syntax colours underneath are the expensive
" part you are not really using. So above a size threshold, drop syntax in
" diff windows only. Normal editing is untouched.
"
" :DiffSyntax toggles it back on. g:gitdiff_syntax_limit = 0 disables this.
let g:gitdiff_syntax_limit = get(g:, 'gitdiff_syntax_limit', 800)

" Both panes must be handled, not just the current one: a diff repaints every
" visible window, so leaving syntax on in the other pane keeps most of the cost
" (measured: one pane off 167ms, both off 14ms, both on 265ms). Window-entered
" autocmds only fire for the window you land in, so sweep all of them.
"
" The sweep is deferred through a 0ms timer rather than run inside the
" autocmd: switching windows from an autocmd is fragile and re-entrant, and
" win_execute() is no good here because it suppresses the Syntax autocmd that
" `setlocal syntax=` depends on (found the hard way — only one pane applied).
let s:perf_pending = 0

function! s:ApplyHere() abort
    if !&diff
        return
    endif

    if g:gitdiff_syntax_limit > 0 && !exists('b:gitdiff_syntax_off')
                \ && line('$') > g:gitdiff_syntax_limit
        let b:gitdiff_syntax_ft = &filetype
        let b:gitdiff_syntax_off = 1
        setlocal syntax=OFF
    endif
endfunction

function! s:DiffPerfRun(...) abort
    let s:perf_pending = 0
    let l:cur = winnr()
    for l:w in range(1, winnr('$'))
        if getwinvar(l:w, '&diff')
            execute 'noautocmd ' . l:w . 'wincmd w'
            call s:ApplyHere()
        endif
    endfor
    execute 'noautocmd ' . l:cur . 'wincmd w'
endfunction

function! s:DiffPerf() abort
    if !&diff || s:perf_pending
        return
    endif
    let s:perf_pending = 1
    if exists('*timer_start')
        call timer_start(0, function('s:DiffPerfRun'))
    else
        call s:DiffPerfRun()
    endif
endfunction

function! s:ToggleDiffSyntax() abort
    let l:turn_on = exists('b:gitdiff_syntax_off')
    let l:cur = winnr()
    for l:w in range(1, winnr('$'))
        if !getwinvar(l:w, '&diff')
            continue
        endif
        execute 'noautocmd ' . l:w . 'wincmd w'
        if l:turn_on
            execute 'setlocal syntax=' . get(b:, 'gitdiff_syntax_ft', &filetype)
            unlet! b:gitdiff_syntax_off
        else
            let b:gitdiff_syntax_ft = &filetype
            let b:gitdiff_syntax_off = 1
            setlocal syntax=OFF
        endif
    endfor
    execute 'noautocmd ' . l:cur . 'wincmd w'
    echomsg 'diff syntax ' . (l:turn_on ? 'ON (slower to scroll)' : 'OFF (faster)')
endfunction

command! DiffSyntax call s:ToggleDiffSyntax()

augroup gitdiff_perf
    autocmd!
    autocmd BufWinEnter,WinEnter,FileType,VimEnter * call s:DiffPerf()
augroup END
