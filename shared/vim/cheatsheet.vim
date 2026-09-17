" ===========================================================================
" Interactive viewer for cheatsheet.txt.
"
" The text file stays the single source of content (plain, greppable, easy to
" edit); this file is only the reader. It opens collapsed to a list of section
" titles so the pane reads as an index rather than 100 lines of everything at
" once, and expands or filters on demand.
"
" Keys inside the pane:
"   <CR> / <Tab>   toggle the section under the cursor
"   zR / zM        expand / collapse everything
"   f              filter by substring (shows matches under their sections)
"   F              clear the filter
"   J / K          jump to next / previous section
"   q / <Esc>      close
" ===========================================================================

if exists('g:loaded_cheatsheet')
    finish
endif
let g:loaded_cheatsheet = 1

let s:dir  = fnamemodify(resolve(expand('<sfile>:p')), ':h')
let s:file = s:dir . '/cheatsheet.txt'
if !filereadable(s:file)
    let s:file = expand('~/.vim/cheatsheet.txt')
endif

let s:filter = ''
let s:lines  = []

function! s:Source() abort
    if empty(s:lines) && filereadable(s:file)
        let s:lines = readfile(s:file)
    endif
    return s:lines
endfunction

function! s:IsHeader(line) abort
    return a:line =~# '^\s*──'
endfunction

" Build the buffer contents for the current filter, plus the fold level of
" each line (1 from a section header onwards, 0 for the title block).
function! s:Build() abort
    let l:src = s:Source()
    if empty(l:src)
        return [['  cheatsheet.txt not found at:', '  ' . s:file], [0, 0]]
    endif

    if s:filter ==# ''
        let l:out = copy(l:src)
    else
        let l:pat     = '\c' . escape(s:filter, '\.*$^~[]')
        let l:out     = []
        let l:pending = ''
        for l:line in l:src
            if s:IsHeader(l:line)
                let l:pending = l:line
            elseif l:line =~# l:pat
                if l:pending !=# ''
                    if !empty(l:out) | call add(l:out, '') | endif
                    call add(l:out, l:pending)
                    let l:pending = ''
                endif
                call add(l:out, l:line)
            endif
        endfor
        if empty(l:out)
            let l:out = ['', '  No match for "' . s:filter . '"', '',
                       \ '  f  search again', '  F  clear the filter']
        endif
    endif

    let l:levels = []
    let l:seen   = 0
    for l:line in l:out
        if s:IsHeader(l:line) | let l:seen = 1 | endif
        call add(l:levels, l:seen)
    endfor
    return [l:out, l:levels]
endfunction

function! CheatsheetFoldLevel(lnum) abort
    if !exists('b:cs_levels') || a:lnum > len(b:cs_levels)
        return 0
    endif
    if s:IsHeader(getline(a:lnum))
        return '>1'
    endif
    return b:cs_levels[a:lnum - 1]
endfunction

function! CheatsheetFoldText() abort
    let l:title = substitute(getline(v:foldstart), '─\+\s*$', '', '')
    let l:title = substitute(l:title, '^\s*──\s*', '', '')
    let l:title = substitute(l:title, '\s*$', '', '')
    " Count only real entries, not blank separator lines.
    let l:n = 0
    for l:i in range(v:foldstart + 1, v:foldend)
        if getline(l:i) =~# '\S' | let l:n += 1 | endif
    endfor
    return printf('  %s %-26s %2d', (v:folddashes ==# '-' ? '▸' : '▸'), l:title, l:n)
endfunction

function! s:Fill() abort
    let [l:out, l:levels] = s:Build()
    let b:cs_levels = l:levels
    setlocal modifiable noreadonly
    silent %delete _
    call setline(1, l:out)
    setlocal nomodifiable readonly nomodified
    setlocal foldmethod=expr foldexpr=CheatsheetFoldLevel(v:lnum)
    setlocal foldtext=CheatsheetFoldText() fillchars=fold:\  foldcolumn=0
    " Filtering implies you want to see the hits, so open up; otherwise start
    " as an index of section titles.
    let &l:foldlevel = (s:filter ==# '' ? 0 : 99)
    call s:Status()
    normal! gg
endfunction

function! s:Status() abort
    let l:left = s:filter ==# '' ? 'CHEATSHEET' : 'CHEATSHEET  /' . s:filter . '/'
    let &l:statusline = ' ' . l:left . '%=f filter  <CR> open  zR all  q close '
endfunction

function! s:Filter() abort
    call inputsave()
    let l:answer = input('filter: ', s:filter)
    call inputrestore()
    redraw
    let s:filter = l:answer
    call s:Fill()
endfunction

function! s:Clear() abort
    let s:filter = ''
    call s:Fill()
endfunction

function! s:ToggleFold() abort
    if foldlevel('.') == 0
        return
    endif
    if foldclosed('.') == -1
        silent! normal! zc
    else
        silent! normal! zo
    endif
endfunction

function! s:Winnr() abort
    let l:bufnr = bufnr('^cheatsheet$')
    return l:bufnr == -1 ? -1 : bufwinnr(l:bufnr)
endfunction

function! s:Width() abort
    let l:max = 0
    for l:line in s:Source()
        let l:max = max([l:max, strdisplaywidth(l:line)])
    endfor
    return min([max([l:max + 2, 34]), 60])
endfunction

function! ToggleCheatsheet() abort
    let l:win = s:Winnr()
    if l:win != -1
        execute l:win . 'wincmd c'
        return
    endif

    execute 'vsplit cheatsheet'
    execute 'vertical resize ' . s:Width()
    setlocal buftype=nofile bufhidden=hide noswapfile nobuflisted
    setlocal nonumber norelativenumber nowrap nocursorline winfixwidth
    setlocal filetype=cheatsheet

    nnoremap <buffer><silent> q     :close<CR>
    nnoremap <buffer><silent> <Esc> :close<CR>
    nnoremap <buffer><silent> <CR>  :call <SID>ToggleFold()<CR>
    nnoremap <buffer><silent> <Tab> :call <SID>ToggleFold()<CR>
    " No <silent> here: it would suppress the input() prompt and you would be
    " typing the filter blind.
    nnoremap <buffer> f             :call <SID>Filter()<CR>
    nnoremap <buffer><silent> F     :call <SID>Clear()<CR>
    nnoremap <buffer><silent> J     :call search('^\s*──', 'W')<CR>
    nnoremap <buffer><silent> K     :call search('^\s*──', 'bW')<CR>

    call s:Fill()
    wincmd p
endfunction

" --- Public API ------------------------------------------------------------
" :Cheatsheet          open (or close) the pane
" :Cheatsheet <text>   open it filtered to <text> — the fastest way in when you
"                      already know what you are looking for
function! CheatsheetOpen(...) abort
    let s:filter = a:0 ? a:1 : ''
    if s:Winnr() == -1
        call ToggleCheatsheet()
    endif
    let l:win = s:Winnr()
    if l:win != -1
        let l:cur = winnr()
        execute l:win . 'wincmd w'
        call s:Fill()
        execute l:cur . 'wincmd w'
    endif
endfunction

command! -nargs=? Cheatsheet call CheatsheetOpen(<q-args>)

" --- Highlighting ----------------------------------------------------------
augroup cheatsheet_syntax
    autocmd!
    autocmd FileType cheatsheet call s:Syntax()
augroup END

function! s:Syntax() abort
    syntax clear
    syntax match cheatsheetBox    '^[╔║╚╗╝═].*'
    syntax match cheatsheetHead   '^\s*──.*'
    syntax match cheatsheetKey    '^\s\+\zs\S\%(\S\| \S\)*\ze\s\s'
    syntax match cheatsheetNote   '^\s*No match.*'
    highlight default link cheatsheetBox  Special
    highlight default link cheatsheetHead Title
    highlight default link cheatsheetKey  Identifier
    highlight default link cheatsheetNote WarningMsg
    highlight default link Folded         Title
endfunction

" --- Window housekeeping ---------------------------------------------------
" Don't let the pane keep vim alive on :q, or sit there on its own.
augroup cheatsheet_windows
    autocmd!
    autocmd QuitPre  * if s:Winnr() != -1 | execute s:Winnr() . 'wincmd c' | endif
    autocmd WinEnter * if winnr('$') == 1 && bufname('%') ==# 'cheatsheet' | quit | endif
augroup END
