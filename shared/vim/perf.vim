" ===========================================================================
" Redraw performance: measure it, and bisect the usual culprits.
"
"   :ScrollBench   time 50 half-page scrolls with a forced redraw each
"   :Fast          turn the expensive things off (mouse, termguicolors,
"                  syntax, cursorline) and report what changed
"   :Fast!         put them all back
"
" Typical use: :ScrollBench, note the number, :Fast, :ScrollBench again. If
" the second number is much lower, toggle the pieces back one at a time to
" find which one costs. If both numbers are similar, the cost is in the
" terminal emulator, not vim, and no setting here will help.
" ===========================================================================

if exists('g:loaded_vimperf')
    finish
endif
let g:loaded_vimperf = 1

function! s:Bench() abort
    let l:view = winsaveview()
    normal! gg
    redraw
    let l:t = reltime()
    for l:i in range(50)
        execute "normal! \<C-d>"
        redraw
    endfor
    let l:ms = str2float(reltimestr(reltime(l:t))) * 1000
    call winrestview(l:view)
    redraw
    echomsg printf('scroll bench: %.0f ms total, %.1f ms per redraw%s',
        \ l:ms, l:ms / 50, (&diff ? '  (diff mode: two panes per redraw)' : ''))
endfunction

let s:saved = {}

function! s:Fast(restore) abort
    if a:restore
        if empty(s:saved)
            echomsg 'nothing to restore'
            return
        endif
        let &mouse = s:saved.mouse
        let &cursorline = s:saved.cursorline
        if has('termguicolors') | let &termguicolors = s:saved.tgc | endif
        if s:saved.syntax | syntax on | endif
        let s:saved = {}
        echomsg 'restored: mouse, cursorline, termguicolors, syntax'
        return
    endif

    let s:saved = {
        \ 'mouse': &mouse,
        \ 'cursorline': &cursorline,
        \ 'tgc': has('termguicolors') ? &termguicolors : 0,
        \ 'syntax': exists('g:syntax_on'),
        \ }
    set mouse=
    set nocursorline
    if has('termguicolors') | set notermguicolors | endif
    syntax off
    echomsg 'off: mouse, cursorline, termguicolors, syntax  (:Fast! to restore)'
endfunction

command! ScrollBench call s:Bench()
command! -bang Fast   call s:Fast(<bang>0)
