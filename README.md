# ![](Manual/pig.png) Brass



Brass is a Z80 assembler primarily designed for TI graphing calculators and 8-bit Sega consoles.

At the time I developed it most people were using a shareware copy of Telemark Assembler (TASM) for TI calculator development.
Aside from a few bugs and limitations in TASM that I wasn't perfectly happy with,
I really wanted to provide something free for others to use.

In the end I released Brass as freeware, so I succeeded in that regard at least, but as for bugs and limitations -
well, I ended up unleashing a few of my own devising on the community.

This was my first program in C# and my first attempt to write something to parse source code and turn it into a program.
To be honest, I'm amazed it works at all, but as this predates my use of source control I've also been very wary of
tampering with it since as I don't want to risk completely breaking it.

What I'm really trying to say, is:

> **Warning**: This is not a good program. Please do not use it in your new projects.

However, due to the project's history, and a few projects out there that probably do rely on Brass features,
I dug around the mouldering old project directory, found a few backup folders tucked away in dusty corners,
and have tried to assemble a somewhat complete archive of the source code.

I did eventually switch to developing [Brass 3](https://github.com/benryves/Brass3), a plugin-based assembler which is
much more feature-complete and robust. However, it's also rather overcomplicated and also has a pile of its own bugs,
and if you're wondering what happened to Brass 2 in the middle then I think it's best to say that my ability to write
a competent assembler is about as good as my ability to count.
